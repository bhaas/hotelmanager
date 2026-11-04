FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HotelManager.slnx dotnet-tools.json ./
COPY HotelManager.Web/HotelManager.Web.csproj HotelManager.Web/
RUN dotnet restore HotelManager.Web/HotelManager.Web.csproj
COPY . .
RUN dotnet publish HotelManager.Web/HotelManager.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Fonts for QuestPDF invoice rendering.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HotelManager.Web.dll"]
