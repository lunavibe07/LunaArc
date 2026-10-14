using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

namespace LunaArc.Studio.ViewModels;

public partial class PasswordRecoveryViewModel : ObservableObject
{
    private readonly StandardArchiveHandler _backend = new();
    private readonly PasswordVault _vault;
    private LunaJobController? _job;
    private bool _paused;
    private readonly Stopwatch _timer = new();

    public PasswordRecoveryViewModel(PasswordVault? vault = null) => _vault = vault ?? new();

    [ObservableProperty] private string _archivePath = string.Empty;
    [ObservableProperty] private string _mask = "Luna202?";
    [ObservableProperty] private string _candidateText = string.Empty;
    [ObservableProperty] private string _masterPassword = string.Empty;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string _status = "Choose an archive and a search method.";
    [ObservableProperty] private string _recoveredPassword = string.Empty;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _canOpen;
    [ObservableProperty] private string _pauseLabel = "Pause";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;
    public bool IsIdle => !IsBusy;

    partial void OnArchivePathChanged(string value)
    {
        CanOpen = false;
        RecoveredPassword = string.Empty;
    }

    private string[] GetCandidates()
    {
        string[] values = CandidateText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Where(s => s.Length != 0).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0 || values.Length > PasswordRecovery.MaximumCandidates)
            throw new ArgumentException("Enter between 1 and 1,000,000 passwords, one per line.");
        return values;
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsBusy) return;
        CanOpen = false;
        RecoveredPassword = string.Empty;
        Percent = 0;
        try
        {
            if (!File.Exists(ArchivePath)) throw new FileNotFoundException("Choose an existing archive.");
            string path = ArchivePath;
            IEnumerable<string> candidates;
            long total;
            if (ModeIndex == 1) { total = PasswordRecovery.CountMask(Mask); candidates = PasswordRecovery.EnumerateMask(Mask); }
            else { var list = GetCandidates(); candidates = list; total = list.Length; }
            _job = new LunaJobController();
            IsBusy = true;
            Status = "Checking the archive...";
            _timer.Restart();
            var job = _job;
            var progress = new Progress<RecoveryProgress>(p =>
            {
                if (!ReferenceEquals(_job, job)) return;
                Percent = 100d * p.Attempted / p.Total;
                Status = $"{p.Attempted:N0} / {p.Total:N0} candidates tested | {p.Attempted / Math.Max(.001, _timer.Elapsed.TotalSeconds):N1} / sec";
            });
            var result = await Task.Run(async () =>
            {
                if (await PasswordRecovery.VerifyArchiveAsync(_backend, path, null, job.Token))
                    return new RecoveryResult(true, null);
                return await PasswordRecovery.SearchAsync(candidates, total,
                    (candidate, ct) => PasswordRecovery.VerifyArchiveAsync(_backend, path, candidate, ct), job, progress);
            });
            CanOpen = result.Verified;
            RecoveredPassword = result.Password ?? string.Empty;
            Status = result.Verified
                ? result.Password == null ? "Archive verified without a password." : "Password found and archive verified. Select Open archive."
                : "No candidate verified. The password may be outside this search, or the archive may be damaged.";
        }
        catch (OperationCanceledException) { Status = "Recovery cancelled."; }
        catch (Exception ex) { Status = "Recovery failed: " + ex.Message; }
        finally
        {
            _timer.Stop();
            _job?.Dispose(); _job = null;
            IsBusy = false; _paused = false; PauseLabel = "Pause";
        }
    }

    [RelayCommand]
    public void Pause()
    {
        if (_job == null) return;
        _paused = !_paused;
        if (_paused) _job.Pause(); else _job.Resume();
        PauseLabel = _paused ? "Resume" : "Pause";
    }

    [RelayCommand] public void Cancel() => _job?.Cancel();

    public async Task LoadVaultAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            string master = MasterPassword;
            var values = await Task.Run(() => _vault.Load(master));
            CandidateText = string.Join("\n", values);
            Status = $"Loaded {values.Count:N0} passwords from the encrypted vault.";
        }
        catch (System.Security.Cryptography.CryptographicException) { Status = "Vault could not be unlocked: wrong master password or damaged data."; }
        catch (Exception ex) { Status = "Vault could not be loaded: " + ex.Message; }
        finally { MasterPassword = string.Empty; IsBusy = false; }
    }

    public async Task SaveVaultAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var values = GetCandidates();
            string master = MasterPassword;
            await Task.Run(() => _vault.Save(values, master));
            Status = $"Saved {values.Length:N0} passwords in the encrypted vault.";
        }
        catch (System.Security.Cryptography.CryptographicException) { Status = "Vault could not be unlocked: wrong master password or damaged data."; }
        catch (Exception ex) { Status = "Vault could not be saved: " + ex.Message; }
        finally { MasterPassword = string.Empty; IsBusy = false; }
    }

    public void ClearSecrets()
    {
        Cancel();
        CandidateText = MasterPassword = RecoveredPassword = string.Empty;
        CanOpen = false;
    }
}
