using System.Globalization;
using System.Resources;

namespace Guanacaste3D.Core;

/// <summary>Localizes persisted default names without requiring a desktop resource dictionary in the core model.</summary>
public static class DefaultLabels
{
    private static readonly ResourceManager Resources = new(typeof(DefaultLabels).FullName!, typeof(DefaultLabels).Assembly);

    /// <summary>Resolves the product's localized initial names; user-authored names remain ordinary model values.</summary>
    public static string Get(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);
}
