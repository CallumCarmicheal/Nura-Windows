using NuraLib.Devices;
using NuraPopupWpf.Models;

namespace NuraPopupWpf.ViewModels;

public sealed partial class MainViewModel {
    public bool ShowAncControl => CurrentDevice.SupportsAnc;

    public bool ShowPassthroughControl =>
        CurrentDevice.SupportsAnc &&
        (!CurrentDevice.IsLive || !(CurrentDevice.LiveDevice?.Info.Supports(NuraAudioCapabilities.GlobalAncToggle) ?? false));

    public bool ShowAncLevelControl => CurrentDevice.SupportsAncLevel;

    public bool ShowSpatialControl => CurrentDevice.SupportsSpatial;

    public bool ShowTouchButtonControls => CurrentDevice.SupportsTouchButtons;

    public bool ShowDialControls => CurrentDevice.SupportsDial;

    public bool ShowEuVolumeLimiterControl => !CurrentDevice.IsLive && CurrentDevice.SupportsEuVolumeLimiter;

    public bool ShowDoubleTapBindings => CurrentDevice.SupportsGesture(NuraButtonGesture.DoubleTap);

    public bool ShowTripleTapBindings => CurrentDevice.SupportsGesture(NuraButtonGesture.TripleTap);

    public bool ShowTapAndHoldBindings => CurrentDevice.SupportsGesture(NuraButtonGesture.TapAndHold);

    public bool CanChangeImmersionControl => CurrentDevice.CanChangeImmersionControl;

    public int CurrentAncLevelValue {
        get => CurrentDevice.AncLevel ?? 0;
        set {
            if (CurrentDevice.AncLevel == value) {
                return;
            }

            CurrentDevice.AncLevel = value;
            OnPropertyChanged(nameof(CurrentAncLevelValue));
            OnPropertyChanged(nameof(CurrentAncLevelText));
        }
    }

    public string CurrentAncLevelText => CurrentDevice.AncLevel?.ToString() ?? "Unknown";

    public ButtonFunctionOption? SelectedLeftSingleTapButtonFunction {
        get => FindButtonFunctionOption(SingleTapButtonOptions, CurrentDevice.TouchButtons?.LeftSingleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.SingleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightSingleTapButtonFunction {
        get => FindButtonFunctionOption(SingleTapButtonOptions, CurrentDevice.TouchButtons?.RightSingleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.SingleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftDoubleTapButtonFunction {
        get => FindButtonFunctionOption(DoubleTapButtonOptions, CurrentDevice.TouchButtons?.LeftDoubleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.DoubleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightDoubleTapButtonFunction {
        get => FindButtonFunctionOption(DoubleTapButtonOptions, CurrentDevice.TouchButtons?.RightDoubleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.DoubleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftTripleTapButtonFunction {
        get => FindButtonFunctionOption(TripleTapButtonOptions, CurrentDevice.TouchButtons?.LeftTripleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.TripleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightTripleTapButtonFunction {
        get => FindButtonFunctionOption(TripleTapButtonOptions, CurrentDevice.TouchButtons?.RightTripleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.TripleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftTapAndHoldButtonFunction {
        get => FindButtonFunctionOption(TapAndHoldButtonOptions, CurrentDevice.TouchButtons?.LeftTapAndHold);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.TapAndHold, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightTapAndHoldButtonFunction {
        get => FindButtonFunctionOption(TapAndHoldButtonOptions, CurrentDevice.TouchButtons?.RightTapAndHold);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.TapAndHold, value?.Value);
    }

    public DialFunctionOption? SelectedLeftDialFunction {
        get => FindDialFunctionOption(CurrentDevice.Dial?.Left ?? NuraDialFunction.None);
        set => SetDialFunction(NuraDialSide.Left, value?.Value ?? NuraDialFunction.None);
    }

    public DialFunctionOption? SelectedRightDialFunction {
        get => FindDialFunctionOption(CurrentDevice.Dial?.Right ?? NuraDialFunction.None);
        set => SetDialFunction(NuraDialSide.Right, value?.Value ?? NuraDialFunction.None);
    }

    private void RefreshCurrentDeviceBindings() {
        OnPropertyChanged(nameof(CurrentProfiles));
        OnPropertyChanged(nameof(CurrentProfileCount));
        OnPropertyChanged(nameof(CurrentBatteryText));
        OnPropertyChanged(nameof(DisplaySerial));
        OnPropertyChanged(nameof(CurrentSoftwareVersion));
        OnPropertyChanged(nameof(CurrentConnectionStatusText));
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsPersonalised));
        OnPropertyChanged(nameof(ImmersionIndex));
        OnPropertyChanged(nameof(CurrentImmersionValue));
        OnPropertyChanged(nameof(CurrentVisualImmersionValue));
        OnPropertyChanged(nameof(IsCurrentDeviceConnected));
        OnPropertyChanged(nameof(IsCurrentDeviceDisconnected));
        OnPropertyChanged(nameof(ShowDisconnectedDevicePlaceholder));
        OnPropertyChanged(nameof(ShowDisconnectedDeviceProfilePreview));
        OnPropertyChanged(nameof(CanInteractWithCurrentDeviceControls));
        OnPropertyChanged(nameof(ShowCurrentDeviceActionPanel));
        OnPropertyChanged(nameof(CurrentDeviceActionText));
        OnPropertyChanged(nameof(ShowAncControl));
        OnPropertyChanged(nameof(ShowPassthroughControl));
        OnPropertyChanged(nameof(ShowAncLevelControl));
        OnPropertyChanged(nameof(ShowSpatialControl));
        OnPropertyChanged(nameof(ShowTouchButtonControls));
        OnPropertyChanged(nameof(ShowDialControls));
        OnPropertyChanged(nameof(ShowEuVolumeLimiterControl));
        OnPropertyChanged(nameof(ShowDoubleTapBindings));
        OnPropertyChanged(nameof(ShowTripleTapBindings));
        OnPropertyChanged(nameof(ShowTapAndHoldBindings));
        OnPropertyChanged(nameof(CanChangeImmersionControl));
        OnPropertyChanged(nameof(CurrentAncLevelValue));
        OnPropertyChanged(nameof(CurrentAncLevelText));

        RefreshInteractionOptionSets();
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void RefreshInteractionOptionSets() {
        ReplaceCollection(_singleTapButtonOptions, BuildButtonFunctionOptions(NuraButtonGesture.SingleTap));
        ReplaceCollection(_doubleTapButtonOptions, BuildButtonFunctionOptions(NuraButtonGesture.DoubleTap));
        ReplaceCollection(_tripleTapButtonOptions, BuildButtonFunctionOptions(NuraButtonGesture.TripleTap));
        ReplaceCollection(_tapAndHoldButtonOptions, BuildButtonFunctionOptions(NuraButtonGesture.TapAndHold));
        ReplaceCollection(_dialFunctionOptions, BuildDialFunctionOptions());
    }

    private void RaiseInteractionSelectionPropertiesChanged() {
        OnPropertyChanged(nameof(SelectedLeftSingleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedRightSingleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedLeftDoubleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedRightDoubleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedLeftTripleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedRightTripleTapButtonFunction));
        OnPropertyChanged(nameof(SelectedLeftTapAndHoldButtonFunction));
        OnPropertyChanged(nameof(SelectedRightTapAndHoldButtonFunction));
        OnPropertyChanged(nameof(SelectedLeftDialFunction));
        OnPropertyChanged(nameof(SelectedRightDialFunction));
    }

    private IReadOnlyList<ButtonFunctionOption> BuildButtonFunctionOptions(NuraButtonGesture gesture) {
        if (!CurrentDevice.SupportsTouchButtons || !CurrentDevice.SupportsGesture(gesture)) {
            return [new ButtonFunctionOption("None", NuraButtonFunction.None)];
        }

        var options = new Dictionary<NuraButtonFunction, ButtonFunctionOption> {
            [NuraButtonFunction.None] = new("None", NuraButtonFunction.None)
        };

        void Add(NuraButtonFunction function, string label) {
            options.TryAdd(function, new ButtonFunctionOption(label, function));
        }

        Add(NuraButtonFunction.PlayPauseAndCall, "Play / pause + call");
        Add(NuraButtonFunction.PlayPauseOnly, "Play / pause");
        Add(NuraButtonFunction.PreviousTrack, "Previous track");
        Add(NuraButtonFunction.NextTrack, "Next track");
        Add(NuraButtonFunction.VolumeUp, "Volume up");
        Add(NuraButtonFunction.VolumeDown, "Volume down");
        Add(NuraButtonFunction.ToggleSocial, "Toggle social mode");
        Add(NuraButtonFunction.ToggleAnc, "Toggle ANC");
        Add(NuraButtonFunction.VoiceAssistant, "Voice assistant");

        if (gesture == NuraButtonGesture.TapAndHold) {
            Add(NuraButtonFunction.HoldForPassthroughOnOneSide, "Hold for passthrough");
            Add(NuraButtonFunction.HoldForPassthroughOnBothSides, "Hold for passthrough (both)");
        } else {
            Add(NuraButtonFunction.TogglePassthroughOnOneSide, "Toggle passthrough");
            Add(NuraButtonFunction.TogglePassthroughOnBothSides, "Toggle passthrough (both)");
        }

        if (CanChangeImmersionControl) {
            Add(NuraButtonFunction.KickItUp, "Immersion up");
            Add(NuraButtonFunction.KickItDown, "Immersion down");
            Add(NuraButtonFunction.ToggleKickIt, "Toggle personalisation");
        }

        if (CurrentDevice.SupportsSpatial) {
            Add(NuraButtonFunction.ToggleSpatial, "Toggle spatial audio");
        }

        return options.Values.ToList();
    }

    private IReadOnlyList<DialFunctionOption> BuildDialFunctionOptions() {
        if (!CurrentDevice.SupportsDial) {
            return [new DialFunctionOption("None", NuraDialFunction.None)];
        }

        var options = new List<DialFunctionOption> {
            new("None", NuraDialFunction.None),
            new("Volume", NuraDialFunction.Volume)
        };

        if (CurrentDevice.SupportsAncLevel || CurrentDevice.SupportsAnc) {
            options.Add(new DialFunctionOption("ANC", NuraDialFunction.Anc));
        }

        if (CanChangeImmersionControl) {
            options.Add(new DialFunctionOption("Immersion", NuraDialFunction.Kickit));
        }

        return options;
    }

    private void SetTouchButtonBinding(NuraButtonSide side, NuraButtonGesture gesture, NuraButtonFunction? function) {
        if (!CurrentDevice.SupportsTouchButtons) {
            return;
        }

        var currentConfiguration = CurrentDevice.TouchButtons ?? new NuraButtonConfiguration();
        var nextConfiguration = currentConfiguration.WithBinding(side, gesture, function ?? NuraButtonFunction.None);
        if (Equals(currentConfiguration, nextConfiguration)) {
            return;
        }

        CurrentDevice.TouchButtons = nextConfiguration;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void SetDialFunction(NuraDialSide side, NuraDialFunction function) {
        if (!CurrentDevice.SupportsDial) {
            return;
        }

        var currentConfiguration = CurrentDevice.Dial ?? new NuraDialConfiguration();
        var nextConfiguration = currentConfiguration.WithBinding(side, function);
        if (Equals(currentConfiguration, nextConfiguration)) {
            return;
        }

        CurrentDevice.Dial = nextConfiguration;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private ButtonFunctionOption? FindButtonFunctionOption(IEnumerable<ButtonFunctionOption> options, NuraButtonFunction? function) {
        return options.FirstOrDefault(option => option.Value == (function ?? NuraButtonFunction.None))
            ?? options.FirstOrDefault();
    }

    private DialFunctionOption? FindDialFunctionOption(NuraDialFunction function) {
        return DialFunctionOptions.FirstOrDefault(option => option.Value == function)
            ?? DialFunctionOptions.FirstOrDefault();
    }

    private void SyncCurrentProfileSelectionFromCurrentDevice(bool animate) {
        var nextProfile = ResolveCurrentProfileSelection();
        if (ReferenceEquals(_currentProfile, nextProfile)) {
            return;
        }

        _suppressProfileSelectionApply = true;
        _currentProfile = nextProfile;
        OnPropertyChanged(nameof(CurrentProfile));
        _suppressProfileSelectionApply = false;

        if (animate) {
            StartProfileAnimation(profileChanged: true, modeChanged: false);
        } else {
            UpdateProfileImage();
        }
    }

    private ProfileModel ResolveCurrentProfileSelection() {
        if (CurrentProfiles.Count == 0) {
            return GetFallbackProfile("Profile 1", 0);
        }

        if (CurrentDevice.CurrentProfileId is int profileId && profileId >= 0 && profileId < CurrentProfiles.Count) {
            return CurrentProfiles[profileId];
        }

        if (CurrentProfiles.Contains(_currentProfile)) {
            return _currentProfile;
        }

        return CurrentProfiles[0];
    }
}
