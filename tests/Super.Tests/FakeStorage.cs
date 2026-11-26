using Microsoft.JSInterop;

namespace Super.Tests;

/// <summary>localStorage de mentira, compartido entre instancias (como reabrir la app).</summary>
internal sealed class FakeStorage : IJSRuntime
{
    public Dictionary<string, string> Values { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        switch (identifier)
        {
            case "superApp.storageGet":
                return ValueTask.FromResult((TValue)(object?)Values.GetValueOrDefault((string)args![0]!)!);
            case "superApp.storageSet":
                Values[(string)args![0]!] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            default:
                throw new InvalidOperationException(identifier);
        }
    }
}
