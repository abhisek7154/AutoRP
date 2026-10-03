using AutoRP.Models;
using AutoRP.ViewModels;
using System.Windows;

namespace AutoRP.UI;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel viewModel;

    public ProfileEditorWindow(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    public RpcProfile? Profile { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Profile = viewModel.CreateProfile();
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid profile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
