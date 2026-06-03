using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UpdateProgressDialog : ContentDialog, INotifyPropertyChanged
{
    private string _progressText = "Preparing...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText != value)
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }
    }

    static UpdateProgressDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(UpdateProgressDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public UpdateProgressDialog()
    {
        InitializeComponent();
        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
            
        DataContext = this;

        DialogHelper.HideActionButtonsArea(this);

        Closing += (s, e) =>
        {
            if (!IsProgrammaticClose)
                e.Cancel = true;
        };
    }

    public bool IsProgrammaticClose { get; set; } = false;

    public void CloseDialog()
    {
        IsProgrammaticClose = true;
        this.Hide();
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
