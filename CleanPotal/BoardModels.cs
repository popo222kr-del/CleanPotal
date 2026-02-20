using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

public enum CellKind { Empty, S2, HF, Rinse }

public sealed class CellVM : INotifyPropertyChanged
{
    public int RowIndex { get; }
    public int SlotIndex { get; }

    private string _text = "";
    private CellKind _kind = CellKind.Empty;

    public string Text { get => _text; set { _text = value; OnPropertyChanged(); } }
    public CellKind Kind { get => _kind; set { _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(BgColor)); } }

    // 엑셀 느낌(필요하면 색만 바꿔)
    public Color BgColor => Kind switch
    {
        CellKind.S2 => Color.FromRgb(255, 0, 0),
        CellKind.HF => Color.FromRgb(255, 255, 0),
        CellKind.Rinse => Color.FromRgb(0, 176, 240),
        _ => Colors.White
    };

    public CellVM(int rowIndex, int slotIndex)
    {
        RowIndex = rowIndex;
        SlotIndex = slotIndex;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BoardRowVM
{
    public string EquipmentName { get; }
    public ObservableCollection<CellVM> Cells { get; } = new();

    public BoardRowVM(string equipmentName, int rowIndex, int totalSlots)
    {
        EquipmentName = equipmentName;
        for (int i = 0; i < totalSlots; i++)
            Cells.Add(new CellVM(rowIndex, i));
    }
}
