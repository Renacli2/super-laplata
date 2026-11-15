using System.Text.Json;
using Microsoft.JSInterop;

namespace Super.Web.Services;

/// <summary>Ajustes de este dispositivo: el nombre (para firmar los pedidos) y si es quien hace las compras.</summary>
public sealed class Settings(IJSRuntime js)
{
    private const string StorageKey = "super.ajustes.v1";
    private bool _loaded;

    public string Name { get; private set; } = "";
    public bool IsAdmin { get; private set; }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("superApp.storageGet", StorageKey);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<Stored>(json) is { } s)
            {
                Name = s.Name ?? "";
                IsAdmin = s.IsAdmin;
            }
        }
        catch (Exception ex) when (ex is JSException or JsonException) { }
    }

    public Task SetNameAsync(string name) => SaveAsync(name.Trim(), IsAdmin);

    public Task SetAdminAsync(bool isAdmin) => SaveAsync(Name, isAdmin);

    private async Task SaveAsync(string name, bool isAdmin)
    {
        Name = name.Length > 40 ? name[..40] : name;
        IsAdmin = isAdmin;
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("superApp.storageSet", StorageKey, JsonSerializer.Serialize(new Stored(Name, IsAdmin)));
        }
        catch (JSException) { }
    }

    private sealed record Stored(string? Name, bool IsAdmin);
}
