namespace ProcessProxyManager.Native;

public sealed record ConnectionSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<NetworkConnectionSnapshot> Connections);
