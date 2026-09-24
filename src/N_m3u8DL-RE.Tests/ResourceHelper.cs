using System.Reflection;

namespace N_m3u8DL_RE.Tests;

public static class ResourceHelper
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    private const string ResourcePrefix = "N_m3u8DL_RE.Tests.Resources.";

    public static string Read(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = _assembly.GetManifestResourceStream(resourceName)
                           ?? throw new ArgumentException($"Embedded resource not found: {resourceName}", nameof(fileName));
        
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}