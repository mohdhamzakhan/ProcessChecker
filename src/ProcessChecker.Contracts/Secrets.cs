using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProcessChecker.Contracts;

public static class DockerSecrets
{
    /// Reads /run/secrets/<name>. A file called ConnectionStrings__Default sets ConnectionStrings:Default.
    public static ConfigurationManager AddDockerSecrets(this ConfigurationManager cfg, string dir = "/run/secrets")
    {
        cfg.AddKeyPerFile(dir, optional: true);
        return cfg;
    }
}

public static class SecretCheck
{
    private static readonly string[] Placeholders = { "YourPassword", "ChangeMe", "put-a-long", "changeme" };

    /// In Production a missing, short or example secret stops the service. In Development it only warns.
    public static void Run(IConfiguration cfg, IHostEnvironment env, ILogger log, params (string Key, int MinLength)[] rules)
    {
        var problems = new List<string>();
        foreach (var (key, min) in rules)
        {
            var v = cfg[key];
            if (string.IsNullOrWhiteSpace(v)) problems.Add($"{key} is not set.");
            else if (v.Length < min) problems.Add($"{key} is shorter than {min} characters.");
            else if (Placeholders.Any(p => v.Contains(p, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{key} still has an example value.");
        }
        if (problems.Count == 0) return;

        var text = "Configuration problems: " + string.Join(" ", problems);
        if (env.IsProduction())
            throw new InvalidOperationException(text + " Fix them before running in Production (see project.md, Secrets).");
        log.LogWarning("{Problems}", text);
    }
}