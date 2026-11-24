using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

/// <summary>
/// Manages curated 3D Mandelbulb landmarks and user-saved 3D spots: scrollable
/// dropdown selector, save-with-name modal, hotkey cycling (, / . / PgUp / PgDn)
/// with canvas toast, and Copy CLI clipboard export.
/// </summary>
public sealed class MandelbulbBookmarks
{
    const int RowHeight = 26;
    const int HeaderHeight = 20;
    const int MaxPopupHeight = 420;
    const int MaxNameLength = 64;

    readonly string _storePath;
    readonly List<MandelbulbBookmarkEntry> _entries;

    int _selectedIndex = -1;
    bool _dropdownOpen;
    float _dropdownScroll;
    Rectangle _triggerRect;
    bool _openedThisFrame;

    bool _saveModalOpen;
    string _nameBuffer = "";
    string _fallbackName = "";
    string _modalSummary = "";
    double _copiedFeedbackUntil;
    double _savedFeedbackUntil;
    string _cycleToastText = "";
    double _cycleToastUntil;

    public MandelbulbBookmarks(string? storePath = null)
    {
        _storePath = string.IsNullOrWhiteSpace(storePath) ? MandelbulbBookmarkStore.ResolveDefaultPath() : storePath;
        _entries = MandelbulbBookmarkStore.LoadOrSeed(_storePath);
    }

    public string StorePath => _storePath;
    public IReadOnlyList<MandelbulbBookmarkEntry> Entries => _entries;
    public int SelectedIndex => _selectedIndex;
    public string? SelectedName => _selectedIndex >= 0 && _selectedIndex < _entries.Count
        ? _entries[_selectedIndex].DisplayName
        : null;
    public bool IsDropdownOpen => _dropdownOpen;
    public bool IsSaveModalOpen => _saveModalOpen;
    public bool CapturesInput => _dropdownOpen || _saveModalOpen;
    public bool CapturesKeyboard => _saveModalOpen;
    public string LastCopiedCommand { get; private set; } = "";

    public void ClearSelection() => _selectedIndex = -1;

    public void DismissPopups()
    {
        _dropdownOpen = false;
        _saveModalOpen = false;
    }

    public void OpenSavePrompt(MandelbulbParams bulb, BulbCameraMode mode)
    {
        _dropdownOpen = false;
        _saveModalOpen = true;
        _openedThisFrame = true;
        _nameBuffer = "";
        _fallbackName = MandelbulbBookmarkEntry.BuildFallbackName(bulb, mode);
        _modalSummary = _fallbackName;
    }

    public string CopyCliCommand(
        BulbCameraMode mode,
        OrbitCameraState orbit,
        CameraPose flyPose,
        MandelbulbParams bulb,
        ColoringParams coloring,
        SsaaMode ssaa)
    {
        string command = MandelbulbBookmarkEntry.BuildCliCommand(mode, orbit, flyPose, bulb, coloring, ssaa);
        LastCopiedCommand = command;
        Raylib.SetClipboardText(command);
        _copiedFeedbackUntil = Raylib.GetTime() + 2.0;
        return command;
    }

    public MandelbulbBookmarkEntry SaveCurrent(
        string? name,
        BulbCameraMode mode,
        OrbitCameraState orbit,
        CameraPose flyPose,
        float flySpeed,
        MandelbulbParams bulb,
        ColoringParams coloring)
    {
        var entry = MandelbulbBookmarkEntry.FromCurrent(name ?? "", mode, orbit, flyPose, flySpeed, bulb, coloring);
        _entries.Add(entry);
        _selectedIndex = _entries.Count - 1;
        MandelbulbBookmarkStore.TrySave(_storePath, _entries);
        _savedFeedbackUntil = Raylib.GetTime() + 2.0;
        return entry;
    }

    public bool DeleteAt(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].Curated)
            return false;

        _entries.RemoveAt(index);
        if (_selectedIndex == index)
            _selectedIndex = -1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        MandelbulbBookmarkStore.TrySave(_storePath, _entries);
        return true;
    }

    public bool ApplyByIndex(int index, MandelbulbFeature bulb, ColoringFeature coloring)
    {
        if (index < 0 || index >= _entries.Count)
            return false;

        MandelbulbBookmarkEntry entry = _entries[index];
        bulb.ApplyBookmarkEntry(entry);
        coloring.SetColoring(entry.ApplyColoring(coloring.State));
        _selectedIndex = index;
        return true;
    }

    public bool ApplyByName(string name, MandelbulbFeature bulb, ColoringFeature coloring)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string target = name.Trim();
        int idx = _entries.FindIndex(e => string.Equals(e.DisplayName, target, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            idx = _entries.FindIndex(e => e.DisplayName.Contains(target, StringComparison.OrdinalIgnoreCase));

        return idx >= 0 && ApplyByIndex(idx, bulb, coloring);
    }

    public bool Cycle(int delta, MandelbulbFeature bulb, ColoringFeature coloring, bool showToast = true)
    {
        if (_entries.Count == 0)
            return false;

        int next;
        if (_selectedIndex < 0)
            next = delta >= 0 ? 0 : _entries.Count - 1;
        else
            next = ((_selectedIndex + delta) % _entries.Count + _entries.Count) % _entries.Count;

        if (!ApplyByIndex(next, bulb, coloring))
            return false;

        if (showToast)
        {
            MandelbulbBookmarkEntry entry = _entries[next];
            string modeTag = entry.CameraMode == BulbCameraMode.Fly ? " [6DOF Fly]" : "";
            _cycleToastText = $"3D Spot {next + 1}/{_entries.Count}: {entry.DisplayName}{modeTag}";
            _cycleToastUntil = Raylib.GetTime() + 2.4;
        }

        return true;
    }

    public bool HandleKeys(MandelbulbFeature bulb, ColoringFeature coloring)
    {
        if (_saveModalOpen)
            return false;

        if (_dropdownOpen)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                _dropdownOpen = false;
            return false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_B))
        {
            OpenSavePrompt(bulb.State, bulb.CameraMode);
            return false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_COMMA) || Raylib.IsKeyPressed(KeyboardKey.KEY_PAGE_UP))
            return Cycle(-1, bulb, coloring);

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_PERIOD) || Raylib.IsKeyPressed(KeyboardKey.KEY_PAGE_DOWN))
            return Cycle(1, bulb, coloring);

        return false;
    }

    public void DrawPanel(
        ref PanelLayout layout,
        MandelbulbFeature bulb,
        ColoringParams coloring,
        SsaaMode ssaa,
        TextEditScope editScope)
    {
        const float labelWidth = 82f;
        RayGui.GuiLabel(new Rectangle { x = layout.X, y = layout.Y, width = labelWidth, height = 28 }, "3D Spot");
        _triggerRect = new Rectangle
        {
            x = layout.X + labelWidth,
            y = layout.Y,
            width = layout.Width - labelWidth,
            height = 28,
        };
        layout.Y += 34;

        bool hovered = !_saveModalOpen && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _triggerRect);
        Color bg = _dropdownOpen
            ? new Color(38, 48, 68, 255)
            : hovered
                ? new Color(36, 44, 62, 255)
                : new Color(30, 34, 46, 255);
        Color border = _dropdownOpen || hovered ? new Color(96, 176, 232, 255) : PanelTheme.PanelBorder;

        Raylib.DrawRectangleRec(_triggerRect, bg);
        Raylib.DrawRectangleLinesEx(_triggerRect, 1f, border);

        string label = _selectedIndex >= 0 && _selectedIndex < _entries.Count
            ? _entries[_selectedIndex].DisplayName
            : "3D landmarks & saved";
        string clipped = FitText(label, (int)_triggerRect.width - 28, 15);
        Raylib.DrawText(clipped, (int)_triggerRect.x + 8, (int)_triggerRect.y + 6, 15, PanelTheme.TitleText);
        Raylib.DrawText(_dropdownOpen ? "^" : "v", (int)(_triggerRect.x + _triggerRect.width - 16), (int)_triggerRect.y + 6, 15, PanelTheme.MutedText);

        if (!_saveModalOpen && hovered && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            editScope.CancelAll();
            _dropdownOpen = !_dropdownOpen;
            if (_dropdownOpen)
            {
                _openedThisFrame = true;
                _dropdownScroll = Math.Max(0f, _dropdownScroll);
            }
        }

        double now = Raylib.GetTime();
        string saveLabel = now < _savedFeedbackUntil ? "Saved!" : "Save spot";
        string copyLabel = now < _copiedFeedbackUntil ? "Copied!" : "Copy CLI";

        if (_dropdownOpen || _saveModalOpen)
            RayGui.GuiDisable();

        layout.TwoButtons(saveLabel, copyLabel, out bool saveClicked, out bool copyClicked);

        if (_dropdownOpen || _saveModalOpen)
            RayGui.GuiEnable();

        if (!_dropdownOpen && !_saveModalOpen)
        {
            if (saveClicked)
            {
                editScope.CancelAll();
                OpenSavePrompt(bulb.State, bulb.CameraMode);
            }

            if (copyClicked)
            {
                CopyCliCommand(bulb.CameraMode, bulb.Camera, bulb.FlyPose, bulb.State, coloring, ssaa);
            }
        }
    }

    public bool DrawOverlay(
        int screenWidth,
        int screenHeight,
        int canvasWidth,
        MandelbulbFeature bulb,
        ColoringFeature coloring,
        TextEditScope editScope)
    {
        bool openedThisFrame = _openedThisFrame;
        _openedThisFrame = false;

        if (_saveModalOpen)
        {
            DrawSaveModal(screenWidth, screenHeight, bulb, coloring.State, openedThisFrame);
            return false;
        }

        if (_dropdownOpen)
            return DrawDropdownPopup(screenWidth, screenHeight, bulb, coloring, editScope, openedThisFrame);

        DrawCycleToast(canvasWidth);
        return false;
    }

    void DrawCycleToast(int canvasWidth)
    {
        if (_cycleToastText.Length == 0 || canvasWidth < 120)
            return;

        double remaining = _cycleToastUntil - Raylib.GetTime();
        if (remaining <= 0.0)
            return;

        float alpha = remaining < 0.4 ? (float)(remaining / 0.4) : 1f;
        const int fontSize = 18;
        int textW = Raylib.MeasureText(_cycleToastText, fontSize);
        int pillW = Math.Min(canvasWidth - 32, textW + 32);
        const int pillH = 36;
        int pillX = (canvasWidth - pillW) / 2;
        const int pillY = 16;

        string shown = FitText(_cycleToastText, pillW - 24, fontSize);
        int shownW = Raylib.MeasureText(shown, fontSize);

        var bg = new Color(16, 20, 30, (int)(225 * alpha));
        var border = new Color(96, 176, 232, (int)(235 * alpha));
        var textCol = new Color(236, 240, 248, (int)(255 * alpha));

        var rect = new Rectangle { x = pillX, y = pillY, width = pillW, height = pillH };
        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 1f, border);
        Raylib.DrawText(shown, pillX + (pillW - shownW) / 2, pillY + 9, fontSize, textCol);
    }

    bool DrawDropdownPopup(
        int screenWidth,
        int screenHeight,
        MandelbulbFeature bulb,
        ColoringFeature coloring,
        TextEditScope editScope,
        bool openedThisFrame)
    {
        var items = BuildListItems();
        int contentHeight = 0;
        foreach (var item in items)
            contentHeight += item.IsHeader ? HeaderHeight : RowHeight;

        float popupWidth = Math.Max(_triggerRect.width + 82f, 296f);
        float popupX = Math.Clamp(_triggerRect.x + _triggerRect.width - popupWidth, 8f, screenWidth - popupWidth - 8f);
        float spaceBelow = screenHeight - (_triggerRect.y + _triggerRect.height) - 12f;
        float spaceAbove = _triggerRect.y - 12f;
        float popupHeight = Math.Min(MaxPopupHeight, Math.Max(80, contentHeight + 8));

        float popupY;
        if (spaceBelow >= popupHeight || spaceBelow >= spaceAbove)
        {
            popupHeight = Math.Min(popupHeight, Math.Max(80f, spaceBelow));
            popupY = _triggerRect.y + _triggerRect.height + 4f;
        }
        else
        {
            popupHeight = Math.Min(popupHeight, Math.Max(80f, spaceAbove));
            popupY = _triggerRect.y - popupHeight - 4f;
        }

        var popupRect = new Rectangle { x = popupX, y = popupY, width = popupWidth, height = popupHeight };
        Vector2 mouse = Raylib.GetMousePosition();
        bool overPopup = Raylib.CheckCollisionPointRec(mouse, popupRect);
        bool overTrigger = Raylib.CheckCollisionPointRec(mouse, _triggerRect);

        float maxScroll = Math.Max(0f, contentHeight - (popupHeight - 8f));
        if (overPopup)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (Math.Abs(wheel) > 0.001f)
                _dropdownScroll = Math.Clamp(_dropdownScroll - wheel * 38f, 0f, maxScroll);
        }
        _dropdownScroll = Math.Clamp(_dropdownScroll, 0f, maxScroll);

        Raylib.DrawRectangle((int)popupX + 4, (int)popupY + 4, (int)popupWidth, (int)popupHeight, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRec(popupRect, new Color(22, 26, 36, 250));
        Raylib.DrawRectangleLinesEx(popupRect, 1f, new Color(96, 176, 232, 255));

        int clipX = (int)popupX + 2;
        int clipY = (int)popupY + 4;
        int clipW = (int)popupWidth - 4;
        int clipH = (int)popupHeight - 8;

        bool applied = false;
        int deleteIndex = -1;
        bool clicked = !openedThisFrame && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT);

        Raylib.BeginScissorMode(clipX, clipY, clipW, clipH);
        float y = popupY + 4f - _dropdownScroll;
        float scrollbarPad = maxScroll > 0.5f ? 10f : 4f;

        foreach (var item in items)
        {
            int h = item.IsHeader ? HeaderHeight : RowHeight;
            if (y + h >= popupY && y <= popupY + popupHeight)
            {
                if (item.IsHeader)
                {
                    Raylib.DrawText(item.Header, (int)popupX + 10, (int)y + 4, 13, new Color(118, 176, 224, 255));
                }
                else
                {
                    MandelbulbBookmarkEntry entry = _entries[item.Index];
                    var rowRect = new Rectangle
                    {
                        x = popupX + 4f,
                        y = y,
                        width = popupWidth - 8f - scrollbarPad,
                        height = RowHeight - 1f,
                    };

                    bool rowHover = overPopup && Raylib.CheckCollisionPointRec(mouse, rowRect);
                    bool selected = item.Index == _selectedIndex;

                    if (selected)
                        Raylib.DrawRectangleRec(rowRect, new Color(46, 74, 108, 255));
                    else if (rowHover)
                        Raylib.DrawRectangleRec(rowRect, new Color(36, 46, 66, 255));

                    var delRect = new Rectangle
                    {
                        x = rowRect.x + rowRect.width - 22f,
                        y = rowRect.y + 3f,
                        width = 18f,
                        height = 18f,
                    };
                    bool showDelete = !entry.Curated;
                    bool delHover = showDelete && overPopup && Raylib.CheckCollisionPointRec(mouse, delRect);

                    string modeBadge = entry.CameraMode == BulbCameraMode.Fly ? " [6DOF]" : "";
                    int textMaxW = (int)(rowRect.width - (showDelete ? 34f : 14f));
                    string text = FitText(entry.DisplayName + modeBadge, textMaxW, 15);
                    Color textColor = selected ? new Color(255, 255, 255, 255) : PanelTheme.TitleText;
                    Raylib.DrawText(text, (int)rowRect.x + 8, (int)rowRect.y + 5, 15, textColor);

                    if (showDelete)
                    {
                        if (delHover)
                            Raylib.DrawRectangleRec(delRect, new Color(165, 58, 66, 255));
                        Raylib.DrawText("x", (int)delRect.x + 5, (int)delRect.y + 1, 15,
                            delHover ? new Color(255, 255, 255, 255) : PanelTheme.MutedText);
                    }

                    if (clicked && rowHover)
                    {
                        if (delHover)
                        {
                            deleteIndex = item.Index;
                        }
                        else
                        {
                            editScope.CancelAll();
                            applied = ApplyByIndex(item.Index, bulb, coloring);
                            _dropdownOpen = false;
                        }
                    }
                }
            }

            y += h;
        }
        Raylib.EndScissorMode();

        if (maxScroll > 0.5f)
        {
            float trackH = popupHeight - 10f;
            float thumbH = Math.Max(24f, trackH * (trackH / contentHeight));
            float thumbY = popupY + 5f + (_dropdownScroll / maxScroll) * (trackH - thumbH);
            Raylib.DrawRectangle((int)(popupX + popupWidth - 7f), (int)thumbY, 4, (int)thumbH, new Color(96, 176, 232, 180));
        }

        if (deleteIndex >= 0)
            DeleteAt(deleteIndex);
        else if (clicked && !overPopup && !overTrigger)
            _dropdownOpen = false;

        return applied;
    }

    void DrawSaveModal(
        int screenWidth,
        int screenHeight,
        MandelbulbFeature bulb,
        ColoringParams coloring,
        bool openedThisFrame)
    {
        if (!openedThisFrame)
        {
            int ch;
            while ((ch = Raylib.GetCharPressed()) > 0)
            {
                if (ch >= 32 && ch <= 126 && _nameBuffer.Length < MaxNameLength)
                    _nameBuffer += (char)ch;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.KEY_BACKSPACE) && _nameBuffer.Length > 0)
                _nameBuffer = _nameBuffer[..^1];
        }

        bool submit = !openedThisFrame && (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ENTER));
        bool cancel = !openedThisFrame && Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE);

        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(8, 10, 16, 165));

        const float modalW = 430f;
        const float modalH = 182f;
        float modalX = (screenWidth - modalW) * 0.5f;
        float modalY = (screenHeight - modalH) * 0.5f;
        var box = new Rectangle { x = modalX, y = modalY, width = modalW, height = modalH };

        Raylib.DrawRectangleRec(box, new Color(24, 28, 38, 255));
        Raylib.DrawRectangleLinesEx(box, 1.5f, new Color(96, 176, 232, 255));

        Raylib.DrawText("Save 3D Mandelbulb spot", (int)modalX + 20, (int)modalY + 18, 22, PanelTheme.TitleText);
        string summary = FitText(_modalSummary, (int)modalW - 40, 14);
        Raylib.DrawText(summary, (int)modalX + 20, (int)modalY + 46, 14, PanelTheme.MutedText);

        var inputRect = new Rectangle { x = modalX + 20, y = modalY + 72, width = modalW - 40, height = 34 };
        Raylib.DrawRectangleRec(inputRect, new Color(16, 19, 27, 255));
        Raylib.DrawRectangleLinesEx(inputRect, 1.5f, new Color(96, 176, 232, 255));

        bool hasText = _nameBuffer.Length > 0;
        string displayText = hasText ? _nameBuffer : _fallbackName;
        Color textColor = hasText ? PanelTheme.TitleText : PanelTheme.MutedText;
        string shown = FitText(displayText, (int)inputRect.width - 22, 17);
        Raylib.DrawText(shown, (int)inputRect.x + 10, (int)inputRect.y + 8, 17, textColor);

        if (((int)(Raylib.GetTime() * 2.2) & 1) == 0)
        {
            int caretX = (int)inputRect.x + 10 + (hasText ? Raylib.MeasureText(shown, 17) : 0);
            Raylib.DrawRectangle(caretX + 1, (int)inputRect.y + 7, 2, 20, new Color(96, 176, 232, 255));
        }

        var cancelRect = new Rectangle { x = modalX + modalW - 210, y = modalY + modalH - 48, width = 90, height = 30 };
        var saveRect = new Rectangle { x = modalX + modalW - 110, y = modalY + modalH - 48, width = 90, height = 30 };

        if (RayGui.GuiButton(cancelRect, "Cancel") != 0)
            cancel = true;
        if (RayGui.GuiButton(saveRect, "Save") != 0)
            submit = true;

        if (!openedThisFrame && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)
            && !Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), box))
        {
            cancel = true;
        }

        if (submit)
        {
            SaveCurrent(_nameBuffer, bulb.CameraMode, bulb.Camera, bulb.FlyPose, bulb.FlySpeed, bulb.State, coloring);
            _saveModalOpen = false;
        }
        else if (cancel)
        {
            _saveModalOpen = false;
        }
    }

    List<ListItem> BuildListItems()
    {
        var items = new List<ListItem>(_entries.Count + 6);
        bool addedSavedHeader = false;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (!_entries[i].Curated)
            {
                if (!addedSavedHeader)
                {
                    items.Add(ListItem.ForHeader("Saved"));
                    addedSavedHeader = true;
                }
                items.Add(ListItem.ForEntry(i));
            }
        }

        string? currentCat = null;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (!_entries[i].Curated)
                continue;

            string cat = string.IsNullOrWhiteSpace(_entries[i].Category) ? "3D Landmarks" : _entries[i].Category;
            if (!string.Equals(cat, currentCat, StringComparison.Ordinal))
            {
                items.Add(ListItem.ForHeader(cat));
                currentCat = cat;
            }
            items.Add(ListItem.ForEntry(i));
        }

        return items;
    }

    static string FitText(string text, int maxWidth, int fontSize)
    {
        if (Raylib.MeasureText(text, fontSize) <= maxWidth)
            return text;

        const string ellipsis = "...";
        int ellipsisW = Raylib.MeasureText(ellipsis, fontSize);
        int len = text.Length;
        while (len > 1 && Raylib.MeasureText(text[..len], fontSize) + ellipsisW > maxWidth)
            len--;

        return text[..len] + ellipsis;
    }

    readonly record struct ListItem(bool IsHeader, string Header, int Index)
    {
        public static ListItem ForHeader(string header) => new(true, header, -1);
        public static ListItem ForEntry(int index) => new(false, "", index);
    }
}
