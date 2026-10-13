using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KosZawal;

public partial class FacilityPopupWindow : Window
{
    public FacilityPopupWindow()
    {
        InitializeComponent();
    }

    public FacilityPopupWindow(FacilityPopupViewModel model) : this()
    {
        DataContext = model;
        Opened += async (_, _) => await model.EnsureLoadedAsync();
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void RefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FacilityPopupViewModel model)
            await model.RefreshAsync(force: true);
    }
}
