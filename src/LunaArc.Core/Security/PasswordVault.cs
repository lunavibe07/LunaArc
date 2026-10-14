using System.Text.Json;

namespace LunaArc.Core.Security;

/// <summary>
/// Stores user-selected passwords in a local JSON file (not encrypted).
/// </summary>
public sealed class PasswordVault
{
    private readonly string _vaultFilePath;
    private List<string> _savedPasswords = new();

    public IReadOnlyList<string> SavedPasswords => _savedPasswords;

    public PasswordVault()
    {
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LunaArc");
        Directory.CreateDirectory(configDir);
        _vaultFilePath = Path.Combine(configDir, "password_vault.json");
        Load();
    }

    public void AddPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || _savedPasswords.Contains(password))
            return;
        _savedPasswords.Add(password);
        Save();
    }

    public void RemovePassword(string password)
    {
        if (_savedPasswords.Remove(password))
            Save();
    }

    private void Load()
    {
        if (!File.Exists(_vaultFilePath)) return;
        try
        {
            string json = File.ReadAllText(_vaultFilePath);
            _savedPasswords = JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch
        {
            _savedPasswords = new();
        }
    }

    private void Save()
    {
        string json = JsonSerializer.Serialize(_savedPasswords, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_vaultFilePath, json);
    }
}
