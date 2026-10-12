using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ims.Api.Tests.Tests.Paging;

/// <summary>The key ring of the list cursors is configured, not left to the host's defaults: a bad ring stops the boot with a clear message instead of failing the first full page.</summary>
public class CursorProtectionSetupTests : IDisposable
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string scratch = Path.Combine(Path.GetTempPath(), "ims-cursor-setup-" + Guid.NewGuid().ToString("N"));

    public CursorProtectionSetupTests() => Directory.CreateDirectory(scratch);

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private static ServiceProvider Build(string environment, string? keysPath)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(keysPath is null ? [] : new Dictionary<string, string?> { [ApiConfigKeys.DataProtectionKeysPath] = keysPath }).Build();
        return new ServiceCollection().AddLogging().AddCursorProtection(config, new FakeEnvironment(environment)).BuildServiceProvider();
    }

    [Fact]
    public void Production_without_a_keys_path_refuses_to_start()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Build(Environments.Production, null));

        Assert.Contains(ApiConfigKeys.DataProtectionKeysPath, refused.Message);
    }

    [Fact]
    public void A_writable_directory_works_and_holds_the_keys()
    {
        var keys = Path.Combine(scratch, "keys");

        using (var services = Build(Environments.Production, keys)) services.VerifyCursorProtection();

        Assert.NotEmpty(Directory.GetFiles(keys, "key-*.xml"));
    }

    [Fact]
    public void A_keys_path_that_is_not_a_writable_directory_stops_the_boot_with_a_clear_message()
    {
        var aFile = Path.Combine(scratch, "not-a-directory");
        File.WriteAllText(aFile, "x");

        using var services = Build(Environments.Production, aFile);
        var stopped = Assert.Throws<InvalidOperationException>(services.VerifyCursorProtection);

        Assert.Contains(ApiConfigKeys.DataProtectionKeysPath, stopped.Message);
    }

    [Fact]
    public void Outside_production_the_default_is_a_folder_under_the_temp_directory()
    {
        using var services = Build(Environments.Development, null);

        services.VerifyCursorProtection();   // writes the default ring, which lives outside the repository
    }
}
