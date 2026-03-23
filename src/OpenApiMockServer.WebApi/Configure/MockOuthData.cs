internal class MockOuthData
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string Type { get; set; }
    public MockOuthData() { }
    public MockOuthData(string clientId, string clientSecret, string type)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Type = type;
    }

    public override bool Equals(object? obj)
    {
        return obj is MockOuthData other &&
               ClientId == other.ClientId &&
               ClientSecret == other.ClientSecret &&
               Type == other.Type;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ClientId, ClientSecret, Type);
    }
}