using KRemote.ViewModels;

namespace KRemote.Views;

public static class AdaptiveLayout
{
    public const double WideThreshold = 900;

    private static readonly GridLength Fill = new(1, GridUnitType.Star);
    private static readonly GridLength Collapsed = new(0);

    public static void Apply(Grid body, MessageListViewModel model)
    {
        if (body.ColumnDefinitions.Count < 2) return;

        if (model.IsWide)
        {
            body.ColumnDefinitions[0].Width = Fill;
            body.ColumnDefinitions[1].Width = Fill;
            body.ColumnSpacing = 12;
            return;
        }

        body.ColumnDefinitions[0].Width = model.ShowList ? Fill : Collapsed;
        body.ColumnDefinitions[1].Width = model.ShowDetail ? Fill : Collapsed;
        body.ColumnSpacing = 0;
    }
}
