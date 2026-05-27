using iCourse.Models;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Models;

public sealed class UserCredentialsTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsAllFields()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new FakeAppPaths(root);
        var credentials = new UserCredentials(paths)
        {
            Username = "u",
            Password = "p",
            LastBatchId = "b",
            AutoLogin = true,
            AutoSelectBatch = true
        };

        var loaded = new UserCredentials(paths);
        loaded.Load();

        Assert.Equal("u", loaded.Username);
        Assert.Equal("p", loaded.Password);
        Assert.Equal("b", loaded.LastBatchId);
        Assert.True(loaded.AutoLogin);
        Assert.True(loaded.AutoSelectBatch);
    }
}
