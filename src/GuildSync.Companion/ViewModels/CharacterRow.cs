using System.Windows.Input;
using Avalonia.Media;

namespace GuildSync.Companion.ViewModels;

public sealed class CharacterRow
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Level { get; init; }
    public required string ItemLevel { get; init; }
    public required string Dkp { get; init; }
    public required string Seen { get; init; }
    public required bool IsMain { get; init; }
    public required ICommand PinCommand { get; init; }
    public required IBrush NameBrush { get; init; }
    public required IBrush DkpBrush { get; init; }
    public string PinLabel => IsMain ? "Main" : "Pin";

    public static IBrush BrushFor(string className)
    {
        var hex = className switch
        {
            "Warrior" => "#c69b6d",
            "Paladin" => "#f48cba",
            "Hunter" => "#abd472",
            "Rogue" => "#fff468",
            "Priest" => "#ffffff",
            "Shaman" => "#0070dd",
            "Mage" => "#3fc7eb",
            "Warlock" => "#9482c9",
            "Monk" => "#00ff96",
            "Druid" => "#ff7c0a",
            "Death Knight" => "#c41e3a",
            "Demon Hunter" => "#a330c9",
            "Evoker" => "#33937f",
            _ => "#e8eaf0",
        };
        return SolidColorBrush.Parse(hex);
    }

    public static IBrush DkpColor(int? dkp)
    {
        var hex = dkp switch
        {
            > 0 => "#4fba5a",
            < 0 => "#d98080",
            _ => "#9aa1ad",
        };
        return SolidColorBrush.Parse(hex);
    }
}
