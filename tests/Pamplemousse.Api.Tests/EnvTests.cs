namespace Pamplemousse.Api.Tests;

public class EnvTests
{
    [Fact]
    public void Get_returns_the_default_when_the_variable_is_unset_or_blank()
    {
        Environment.SetEnvironmentVariable("TG_TEST_UNSET", null);
        Environment.SetEnvironmentVariable("TG_TEST_BLANK", "  ");

        Assert.Equal("fallback", Env.Get("TG_TEST_UNSET", "fallback"));
        Assert.Equal("fallback", Env.Get("TG_TEST_BLANK", "fallback"));
    }

    [Fact]
    public void Get_returns_the_variable_when_set()
    {
        Environment.SetEnvironmentVariable("TG_TEST_SET", "value");

        Assert.Equal("value", Env.Get("TG_TEST_SET", "fallback"));
    }

    [Fact]
    public void LoadFile_reads_values_without_overriding_existing_variables()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "# comment",
                "",
                "TG_TEST_NEW=from-file",
                "TG_TEST_QUOTED=\"with spaces\"",
                "TG_TEST_KEEP=from-file",
                "not a pair",
            ]);
            Environment.SetEnvironmentVariable("TG_TEST_NEW", null);
            Environment.SetEnvironmentVariable("TG_TEST_QUOTED", null);
            Environment.SetEnvironmentVariable("TG_TEST_KEEP", "from-environment");

            Env.LoadFile(path);

            Assert.Equal("from-file", Env.Get("TG_TEST_NEW", ""));
            Assert.Equal("with spaces", Env.Get("TG_TEST_QUOTED", ""));
            Assert.Equal("from-environment", Env.Get("TG_TEST_KEEP", ""));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
