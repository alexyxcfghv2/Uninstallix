using Desinstalleur.App.Services;

namespace Desinstalleur.App.Models;

public enum QueueStatus
{
    Waiting,
    Running,
    Scanning,
    Removed,
    ForcedCleanup,
    StillInstalled,
    Failed,
    Skipped,
}

/// <summary>Un logiciel dans la file de désinstallation.</summary>
public sealed class QueueItem : ObservableObject
{
    private QueueStatus _status = QueueStatus.Waiting;
    private string? _detail;

    public QueueItem(InstalledProgram program) => Program = program;

    public InstalledProgram Program { get; }

    public QueueStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public string? Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public bool IsBusy => Status is QueueStatus.Running or QueueStatus.Scanning;

    public string StatusText => Status switch
    {
        QueueStatus.Waiting => L.T("En attente"),
        QueueStatus.Running => L.T("Désinstallation en cours…"),
        QueueStatus.Scanning => L.T("Recherche des restes…"),
        QueueStatus.Removed => L.T("Désinstallé"),
        QueueStatus.ForcedCleanup => L.T("Nettoyage forcé"),
        QueueStatus.StillInstalled => L.T("Toujours installé"),
        QueueStatus.Failed => L.T("Échec"),
        QueueStatus.Skipped => L.T("Ignoré"),
        _ => string.Empty,
    };
}
