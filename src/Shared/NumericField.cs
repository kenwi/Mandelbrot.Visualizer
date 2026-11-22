using System.Globalization;
using System.Text;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Shared;

/// <summary>
/// Tracks which panel text field currently owns keyboard input across all feature slices.
/// </summary>
public sealed class TextEditScope
{
    object? _activeField;

    public int CancelVersion { get; private set; }
    public bool CapturesKeyboard => _activeField != null;

    public bool IsEditing(object field) => ReferenceEquals(_activeField, field);

    public void Toggle(object field)
        => _activeField = ReferenceEquals(_activeField, field) ? null : field;

    public void CancelAll()
    {
        _activeField = null;
        CancelVersion++;
    }
}

public sealed class DoubleField
{
    const int BufferSize = 64;

    readonly byte[] _buffer = new byte[BufferSize];
    double _shown = double.NaN;
    int _cancelVersion;

    public void Invalidate() => _shown = double.NaN;

    public bool Draw(
        ref PanelLayout layout,
        TextEditScope scope,
        double current,
        Action<double> apply,
        string? label = null)
    {
        if (_cancelVersion != scope.CancelVersion)
        {
            _shown = double.NaN;
            _cancelVersion = scope.CancelVersion;
        }

        bool edit = scope.IsEditing(this);
        if (!edit && (double.IsNaN(_shown) || Math.Abs(_shown - current) > 1e-12))
        {
            TextBuffer.Write(_buffer, current.ToString("0.######", CultureInfo.InvariantCulture));
            _shown = current;
        }

        float boxX = label == null ? layout.X : layout.X + 48;
        float boxWidth = label == null ? layout.Width : layout.Width - 48;
        if (label != null)
            RayGui.GuiLabel(new Rectangle { x = layout.X, y = layout.Y, width = 44, height = 28 }, label);

        var bounds = new Rectangle { x = boxX, y = layout.Y, width = boxWidth, height = 28 };
        layout.Y += 34;

        bool wasEditing = edit;
        if (TextBuffer.DrawBox(bounds, _buffer, edit) != 0)
        {
            scope.Toggle(this);
            edit = scope.IsEditing(this);
        }

        if (!wasEditing || edit) return false;
        if (!TryParse(TextBuffer.Read(_buffer), out double value))
        {
            _shown = double.NaN;
            return false;
        }

        apply(value);
        _shown = double.NaN;
        return true;
    }

    static bool TryParse(string text, out double value)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}

public sealed class IntField
{
    const int BufferSize = 64;

    readonly byte[] _buffer = new byte[BufferSize];
    int _shown = int.MinValue;
    int _cancelVersion;

    public void Invalidate() => _shown = int.MinValue;

    public bool Draw(
        ref PanelLayout layout,
        TextEditScope scope,
        int current,
        Action<int> apply)
    {
        if (_cancelVersion != scope.CancelVersion)
        {
            _shown = int.MinValue;
            _cancelVersion = scope.CancelVersion;
        }

        bool edit = scope.IsEditing(this);
        if (!edit && _shown != current)
        {
            TextBuffer.Write(_buffer, current.ToString(CultureInfo.InvariantCulture));
            _shown = current;
        }

        var bounds = layout.Row(28);
        bool wasEditing = edit;
        if (TextBuffer.DrawBox(bounds, _buffer, edit) != 0)
        {
            scope.Toggle(this);
            edit = scope.IsEditing(this);
        }

        if (!wasEditing || edit) return false;
        if (!int.TryParse(TextBuffer.Read(_buffer), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            _shown = int.MinValue;
            return false;
        }

        apply(value);
        _shown = int.MinValue;
        return true;
    }
}

static class TextBuffer
{
    public static unsafe int DrawBox(Rectangle bounds, byte[] buffer, bool edit)
    {
        fixed (byte* pointer = buffer)
            return RayGui.GuiTextBox(bounds, (sbyte*)pointer, buffer.Length, edit);
    }

    public static void Write(byte[] buffer, string text)
    {
        Array.Clear(buffer);
        int count = Encoding.UTF8.GetBytes(text, buffer.AsSpan(0, buffer.Length - 1));
        buffer[count] = 0;
    }

    public static string Read(byte[] buffer)
    {
        int count = Array.IndexOf(buffer, (byte)0);
        if (count < 0) count = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, count);
    }
}
