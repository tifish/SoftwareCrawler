using System.Reflection;
using JeekTools;
using SoftwareCrawler.Models;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The app reads one flat settings object while two files own the values. These
/// check the two views cannot drift apart.
/// </summary>
public class AppSettingsShapeTests
{
    private static IEnumerable<PropertyInfo> StoredProperties =>
        typeof(MachineAppSettings)
            .GetProperties()
            .Concat(typeof(RoamingAppSettings).GetProperties());

    /// <summary>
    /// Adding a setting to one of the files and forgetting to surface it fails
    /// here rather than by quietly never being read.
    /// </summary>
    [Fact]
    public void EveryStoredSettingIsReachableFromTheFlatView()
    {
        foreach (var stored in StoredProperties)
        {
            var forwarded = typeof(AppSettings).GetProperty(stored.Name);

            Assert.True(forwarded is not null, $"AppSettings does not expose {stored.Name}");
            Assert.Equal(stored.PropertyType, forwarded!.PropertyType);
            Assert.True(forwarded.CanRead && forwarded.CanWrite, $"{stored.Name} is not read-write");
        }
    }

    /// <summary>And nothing may exist only on the flat view, where nothing saves it.</summary>
    [Fact]
    public void TheFlatViewExposesNothingThatIsNotStored()
    {
        var stored = StoredProperties.Select(property => property.Name).ToHashSet();

        var extra = typeof(AppSettings)
            .GetProperties()
            .Where(property =>
                property.Name != nameof(AppSettings.Machine)
                && property.Name != nameof(AppSettings.Roaming)
            )
            .Select(property => property.Name)
            .Where(name => !stored.Contains(name));

        Assert.Empty(extra);
    }

    /// <summary>Writing through the flat view has to reach the object that gets saved.</summary>
    [Fact]
    public void WritingTheFlatViewWritesTheHalfThatOwnsIt()
    {
        var settings = new AppSettings();

        foreach (var stored in StoredProperties)
        {
            var forwarded = typeof(AppSettings).GetProperty(stored.Name)!;
            var owner = stored.DeclaringType == typeof(MachineAppSettings)
                ? (object)settings.Machine
                : settings.Roaming;

            var value = DistinctValueFor(stored, forwarded.GetValue(settings));
            forwarded.SetValue(settings, value);

            Assert.Equal(value, stored.GetValue(owner));
            Assert.Equal(value, forwarded.GetValue(settings));
        }
    }

    /// <summary>Neither half may be written into the other's file.</summary>
    [Fact]
    public void EachHalfSerializesOnlyItsOwnKeys()
    {
        var settings = new AppSettings();

        var machineJson = JsonSettingsFile.Serialize(settings.Machine);
        var roamingJson = JsonSettingsFile.Serialize(settings.Roaming);

        Assert.Contains(nameof(AppSettings.Proxy), machineJson);
        Assert.DoesNotContain(nameof(AppSettings.DownloadTimeout), machineJson);
        Assert.Contains(nameof(AppSettings.DownloadTimeout), roamingJson);
        Assert.DoesNotContain(nameof(AppSettings.Proxy), roamingJson);
    }

    private static object DistinctValueFor(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string))
            return "a distinct value";
        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (int)(current ?? 0) + 17;
        if (type == typeof(List<string>))
            return new List<string> { "a distinct value" };
        if (type.IsEnum)
            return Enum.GetValues(type)
                .Cast<object>()
                .First(value => !Equals(value, current));

        throw new NotSupportedException($"No distinct value known for {type}");
    }
}
