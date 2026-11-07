using Guanacaste3D.Core;

/// <summary>Checks the 1.0.0 … 1.0.99 → 1.1.0 … 1.99.99 → 2.0.0 version order.</summary>
static class AppVersionChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(new AppVersion(1, 0, 0).Next() == new AppVersion(1, 0, 1) &&
              new AppVersion(1, 0, 98).Next() == new AppVersion(1, 0, 99),
            "The patch number counts up by one");
        check(new AppVersion(1, 0, 99).Next() == new AppVersion(1, 1, 0) &&
              new AppVersion(1, 41, 99).Next() == new AppVersion(1, 42, 0),
            "After 99 the patch goes back to 0 and the minor number goes up");
        check(new AppVersion(1, 99, 99).Next() == new AppVersion(2, 0, 0),
            "After 1.99.99 comes 2.0.0");

        var walked = new AppVersion(1, 0, 0);
        for (var step = 0; step < 100 * 100 - 1; step++) walked = walked.Next();
        check(walked == new AppVersion(1, 99, 99) && walked.Next() == new AppVersion(2, 0, 0),
            "Counting from 1.0.0 reaches 1.99.99 after exactly 9,999 releases");

        check(AppVersion.TryParse("1.0.0", out var plain) && plain.ToString() == "1.0.0" &&
              AppVersion.TryParse("1.2.3+5cca960e", out var build) && build == new AppVersion(1, 2, 3),
            "A version reads from plain text and ignores the build hash after a plus");
        check(!AppVersion.TryParse("1.0", out _) && !AppVersion.TryParse("1.100.0", out _) && !AppVersion.TryParse("1.0.100", out _) &&
              !AppVersion.TryParse("1.-1.0", out _) && !AppVersion.TryParse("a.b.c", out _) && !AppVersion.TryParse(null, out _),
            "Text that is not a version of this product, or past 99, is rejected");
        check(new AppVersion(1, 0, 99).CompareTo(new AppVersion(1, 1, 0)) < 0 && new AppVersion(2, 0, 0).CompareTo(new AppVersion(1, 99, 99)) > 0,
            "Versions compare in release order");
    }
}
