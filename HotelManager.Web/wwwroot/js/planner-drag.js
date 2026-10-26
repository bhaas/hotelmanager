// Pointer-based drag for room planner reservations (move + resize).
// Runs fully client-side for smooth feedback and calls .NET only once on drop.
window.plannerDrag = (() => {
    const DAY_MS = 86400000;
    const DRAG_THRESHOLD_PX = 4;
    let board = null;
    let dotNet = null;
    let state = null;
    let suppressClickUntil = 0;

    const toDayNumber = iso => Math.round(Date.parse(iso + "T00:00:00Z") / DAY_MS);
    const toIso = dayNumber => new Date(dayNumber * DAY_MS).toISOString().slice(0, 10);

    function cellAt(x, y) {
        for (const el of document.elementsFromPoint(x, y)) {
            if (el.classList && el.classList.contains("timeline-day-cell") && board.contains(el)) {
                return el;
            }
        }
        return null;
    }

    function clearPreview() {
        board.querySelectorAll(".drag-ghost").forEach(el => el.remove());
    }

    function computeResult(cell) {
        const roomId = cell.dataset.roomId;
        const day = toDayNumber(cell.dataset.day);
        const { mode, checkIn, checkOut, grabOffset, sourceRoomId } = state;

        if (mode === "create") {
            // Bars run from mid-day of arrival to mid-day of departure, so the hovered day is the departure.
            const from = Math.min(checkIn, day);
            const to = Math.max(checkIn, day);
            return { roomId: sourceRoomId, checkIn: from, checkOut: to === from ? from + 1 : to };
        }

        if (mode === "move") {
            const start = day - grabOffset;
            return { roomId, checkIn: start, checkOut: start + (checkOut - checkIn) };
        }

        // Resizing always stays in the original room row.
        if (mode === "resize-start") {
            return { roomId: sourceRoomId, checkIn: day, checkOut };
        }

        return { roomId: sourceRoomId, checkIn, checkOut: day };
    }

    function renderPreview(result) {
        clearPreview();
        if (!result) {
            return;
        }

        const row = board.querySelector(`.timeline-row[data-room-id="${result.roomId}"]`);
        const cells = row ? row.querySelectorAll(".timeline-day-cell") : [];
        if (cells.length === 0) {
            return;
        }

        // Same half-day column math as RoomPlanner.GetPlacement (two grid columns per day).
        const viewStart = toDayNumber(cells[0].dataset.day);
        const days = cells.length;
        const valid = result.checkOut > result.checkIn;
        const checkIn = result.checkIn - viewStart;
        const checkOut = (valid ? result.checkOut : result.checkIn + 1) - viewStart;
        const start = Math.max(1, checkIn * 2 + 2);
        const end = Math.min(days * 2 + 1, checkOut * 2 + 2);
        if (end <= start) {
            return;
        }

        const ghost = document.createElement("div");
        ghost.className = valid ? "drag-ghost" : "drag-ghost drag-ghost-invalid";
        ghost.style.gridColumn = `${start} / ${end}`;
        ghost.style.gridRow = "1";
        ghost.textContent = `${formatDay(result.checkIn)} – ${formatDay(result.checkOut)}`;
        row.appendChild(ghost);
    }

    function formatDay(dayNumber) {
        return new Date(dayNumber * DAY_MS).toLocaleDateString(document.documentElement.lang || undefined,
            { day: "2-digit", month: "short", timeZone: "UTC" });
    }

    function onPointerDown(e) {
        hideCard();
        if (e.button !== 0) {
            return;
        }

        const chip = e.target.closest(".reservation-chip");
        if (!chip) {
            const startCell = e.target.closest(".timeline-day-cell");
            if (!startCell || !board.contains(startCell)) {
                return;
            }

            const day = toDayNumber(startCell.dataset.day);
            state = {
                chip: null,
                mode: "create",
                pointerId: e.pointerId,
                startX: e.clientX,
                startY: e.clientY,
                dragging: false,
                sourceRoomId: startCell.dataset.roomId,
                checkIn: day,
                checkOut: day + 1,
                grabOffset: 0,
                result: { roomId: startCell.dataset.roomId, checkIn: day, checkOut: day + 1 }
            };
            e.preventDefault();
            return;
        }

        if (!board.contains(chip) || chip.dataset.locked === "true") {
            return;
        }

        const handle = e.target.closest(".reservation-resize-handle");
        const mode = handle
            ? (handle.classList.contains("reservation-resize-handle-start") ? "resize-start" : "resize-end")
            : "move";

        const checkIn = toDayNumber(chip.dataset.checkIn);
        const cell = cellAt(e.clientX, e.clientY);
        const grabOffset = cell ? Math.max(0, toDayNumber(cell.dataset.day) - checkIn) : 0;

        state = {
            chip,
            mode,
            pointerId: e.pointerId,
            startX: e.clientX,
            startY: e.clientY,
            dragging: false,
            reservationId: chip.dataset.reservationId,
            sourceRoomId: chip.dataset.roomId,
            checkIn,
            checkOut: toDayNumber(chip.dataset.checkOut),
            grabOffset,
            result: null
        };

        e.preventDefault();
    }

    function onPointerMove(e) {
        if (!state || e.pointerId !== state.pointerId) {
            return;
        }

        if (!state.dragging) {
            if (Math.abs(e.clientX - state.startX) < DRAG_THRESHOLD_PX &&
                Math.abs(e.clientY - state.startY) < DRAG_THRESHOLD_PX) {
                return;
            }

            state.dragging = true;
            board.classList.add("planner-dragging", `planner-dragging-${state.mode}`);
            state.chip?.classList.add("reservation-chip-dragging");
        }

        const cell = cellAt(e.clientX, e.clientY);
        if (cell && (state.mode === "move" || cell.dataset.roomId === state.sourceRoomId)) {
            state.result = computeResult(cell);
        } else if (cell && state.mode !== "move") {
            // Pointer left the row while resizing: keep tracking the day column in the source row.
            state.result = computeResult({ dataset: { roomId: state.sourceRoomId, day: cell.dataset.day } });
        }

        renderPreview(state.result);
    }

    function finish(commit) {
        if (!state) {
            return;
        }

        const { dragging, result, reservationId, mode, chip } = state;
        state = null;
        clearPreview();
        board.classList.remove("planner-dragging", "planner-dragging-move", "planner-dragging-resize-start", "planner-dragging-resize-end", "planner-dragging-create");
        chip?.classList.remove("reservation-chip-dragging");

        // A plain click on a free day also starts a one-night reservation.
        if (mode === "create") {
            if (commit && result && dotNet) {
                dotNet.invokeMethodAsync("OnCreateRangeSelected", result.roomId, toIso(result.checkIn), toIso(result.checkOut));
            }
            return;
        }

        if (!dragging) {
            return;
        }

        suppressClickUntil = Date.now() + 300;
        if (commit && result && dotNet) {
            dotNet.invokeMethodAsync(
                "OnReservationDragCompleted",
                reservationId,
                mode,
                result.roomId,
                toIso(result.checkIn),
                toIso(result.checkOut));
        }
    }

    function onPointerUp(e) {
        if (state && e.pointerId === state.pointerId) {
            finish(true);
        }
    }

    function onKeyDown(e) {
        const target = e.target;
        const isEditing = target instanceof HTMLElement
            && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));

        if (e.key === "Escape") {
            const wasDragging = !!state;
            finish(false);
            hideCard();
            const backdrop = document.querySelector(".context-menu-backdrop");
            if (backdrop) {
                backdrop.click();
            } else if (!wasDragging && !isEditing && !document.querySelector(".modal.show")) {
                document.querySelector(".editor-drawer-close:not(:disabled)")?.click();
            }
            return;
        }

        if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === "z" && !isEditing) {
            const undo = document.querySelector(".planner-toast-undo:not(:disabled)");
            if (undo) {
                e.preventDefault();
                undo.click();
            }
        }
    }

    // ----- Hover card -----
    let card = null;
    let cardTimer = 0;
    let cardChip = null;

    function ensureCard() {
        if (!card) {
            card = document.createElement("div");
            card.className = "planner-hover-card";
            card.setAttribute("role", "tooltip");
            document.body.appendChild(card);
        }
        return card;
    }

    function hideCard() {
        clearTimeout(cardTimer);
        cardChip = null;
        if (card) {
            card.classList.remove("visible");
        }
    }

    function addLine(parent, className, text) {
        const el = document.createElement("div");
        el.className = className;
        el.textContent = text;
        parent.appendChild(el);
        return el;
    }

    function showCard(chip, x, y) {
        const d = chip.dataset;
        const labels = board.querySelector(".planner-board")?.dataset ?? {};
        const nights = Number(d.cardNights);
        const el = ensureCard();
        el.replaceChildren();

        const header = addLine(el, "phc-header", "");
        const badge = document.createElement("span");
        badge.className = `phc-badge ${d.cardStatusClass}`;
        header.appendChild(badge);
        header.appendChild(document.createTextNode(d.cardGuest || "—"));

        addLine(el, "phc-status", d.cardStatus);
        addLine(el, "phc-line", `${labels.labelRoom ?? "Room"} ${d.cardRoom}`);
        addLine(el, "phc-line", d.cardDates);
        const nightsLabel = (nights === 1 ? labels.labelNight ?? "{0} night" : labels.labelNights ?? "{0} nights").replace("{0}", nights);
        addLine(el, "phc-line", `${nightsLabel} · ${d.cardOccupancy} ${labels.labelGuests ?? "guests"}`);
        if (d.cardNotes) {
            addLine(el, "phc-notes", d.cardNotes);
        }

        el.classList.add("visible");
        positionCard(x, y);
    }

    function positionCard(x, y) {
        if (!card) {
            return;
        }
        const margin = 14;
        const rect = card.getBoundingClientRect();
        let left = x + margin;
        let top = y + margin;
        if (left + rect.width > window.innerWidth - 8) {
            left = x - rect.width - margin;
        }
        if (top + rect.height > window.innerHeight - 8) {
            top = y - rect.height - margin;
        }
        card.style.left = `${Math.max(8, left)}px`;
        card.style.top = `${Math.max(8, top)}px`;
    }

    function onHoverMove(e) {
        if (state || document.querySelector(".context-menu")) {
            hideCard();
            return;
        }

        const chip = e.target.closest?.(".reservation-chip");
        if (!chip || !board.contains(chip)) {
            if (cardChip) {
                hideCard();
            }
            return;
        }

        if (chip === cardChip) {
            if (card?.classList.contains("visible")) {
                positionCard(e.clientX, e.clientY);
            }
            return;
        }

        hideCard();
        cardChip = chip;
        const x = e.clientX;
        const y = e.clientY;
        cardTimer = setTimeout(() => {
            if (cardChip === chip && chip.isConnected) {
                showCard(chip, x, y);
            }
        }, 350);
    }

    // ----- Ctrl + wheel zoom -----
    let lastZoomAt = 0;
    function onWheel(e) {
        if (!e.ctrlKey || !e.target.closest?.(".planner-board")) {
            return;
        }
        e.preventDefault();
        const now = Date.now();
        if (now - lastZoomAt < 120 || !dotNet) {
            return;
        }
        lastZoomAt = now;
        dotNet.invokeMethodAsync("OnZoomRequested", e.deltaY < 0 ? 1 : -1);
    }

    function onClickCapture(e) {
        if (Date.now() < suppressClickUntil && e.target.closest(".reservation-chip")) {
            e.stopPropagation();
            e.preventDefault();
        }
    }

    function init(element, dotNetRef) {
        dispose();
        board = element;
        dotNet = dotNetRef;
        board.addEventListener("pointerdown", onPointerDown);
        board.addEventListener("click", onClickCapture, true);
        board.addEventListener("pointermove", onHoverMove);
        board.addEventListener("pointerleave", hideCard);
        board.addEventListener("contextmenu", hideCard);
        board.addEventListener("wheel", onWheel, { passive: false });
        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", onPointerUp);
        document.addEventListener("pointercancel", onPointerUp);
        document.addEventListener("keydown", onKeyDown);
    }

    function dispose() {
        if (board) {
            board.removeEventListener("pointerdown", onPointerDown);
            board.removeEventListener("click", onClickCapture, true);
            board.removeEventListener("pointermove", onHoverMove);
            board.removeEventListener("pointerleave", hideCard);
            board.removeEventListener("contextmenu", hideCard);
            board.removeEventListener("wheel", onWheel);
        }

        document.removeEventListener("pointermove", onPointerMove);
        document.removeEventListener("pointerup", onPointerUp);
        document.removeEventListener("pointercancel", onPointerUp);
        document.removeEventListener("keydown", onKeyDown);
        hideCard();
        card?.remove();
        card = null;
        board = null;
        dotNet = null;
        state = null;
    }

    function scrollIntoView(selector) {
        document.querySelector(selector)?.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    function fitToViewport(selector) {
        const el = document.querySelector(selector);
        if (!el) {
            return;
        }
        const rect = el.getBoundingClientRect();
        if (rect.right > window.innerWidth - 8) {
            el.style.left = `${Math.max(8, window.innerWidth - rect.width - 8)}px`;
        }
        if (rect.bottom > window.innerHeight - 8) {
            el.style.top = `${Math.max(8, window.innerHeight - rect.height - 8)}px`;
        }
    }

    return { init, dispose, scrollIntoView, fitToViewport };
})();
