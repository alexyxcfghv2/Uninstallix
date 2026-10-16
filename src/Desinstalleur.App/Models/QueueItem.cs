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
        QueueStatus.Waiting => "En attente",
        QueueStatus.Running => "Désinstallation en cours…",
        QueueStatus.Scanning => "Recherche des restes…",
        QueueStatus.Removed => "Désinstallé",
        QueueStatus.ForcedCleanup => "Nettoyage forcé",
        QueueStatus.StillInstalled => "Toujours installé",
        QueueStatus.Failed => "Échec",
        QueueStatus.Skipped => "Ignoré",
        _ => string.Empty,
    };
}
