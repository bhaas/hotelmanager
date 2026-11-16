namespace HotelManager.Web.Features.Guests;

/// <summary>Correspondence languages a guest profile can have (ISO 639-1 code and native name).</summary>
public static class GuestLanguages
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("de", "Deutsch"),
        ("en", "English"),
        ("it", "Italiano"),
        ("fr", "Français"),
        ("nl", "Nederlands"),
        ("cs", "Čeština"),
        ("pl", "Polski"),
        ("hu", "Magyar"),
        ("es", "Español"),
        ("da", "Dansk"),
        ("sv", "Svenska"),
        ("sk", "Slovenčina"),
        ("sl", "Slovenščina"),
        ("hr", "Hrvatski"),
        ("ro", "Română"),
        ("ru", "Русский")
    ];
}
