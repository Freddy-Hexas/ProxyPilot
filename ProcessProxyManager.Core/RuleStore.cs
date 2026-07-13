namespace ProcessProxyManager.Core;

public sealed class RuleStore : JsonFileStore<UserRulesDocument>
{
    public RuleStore(string filePath) : base(filePath)
    {
    }
}
