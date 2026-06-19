using NuraLib.Devices;
using NuraPopupWpf.Models;

namespace NuraPopupWpf.ViewModels;

public sealed partial class MainViewModel {
    private int? _pendingImmersionIndex;
    private int? _pendingAncLevelValue;
    private NuraButtonConfiguration? _pendingTouchButtons;
    private NuraDialConfiguration? _pendingDial;

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

    public bool HasPendingImmersionChange => _pendingImmersionIndex.HasValue && _pendingImmersionIndex.Value != CurrentDevice.ImmersionIndex;

    public int CurrentAncLevelValue {
        get => _pendingAncLevelValue ?? CurrentDevice.AncLevel ?? 0;
        set {
            var nextValue = Math.Clamp(value, 0, 4);
            if (CurrentAncLevelValue == nextValue) {
                return;
            }

            _pendingAncLevelValue = nextValue;
            OnPropertyChanged(nameof(CurrentAncLevelValue));
            OnPropertyChanged(nameof(CurrentAncLevelText));
            OnPropertyChanged(nameof(HasPendingAncLevelChange));
        }
    }

    public bool HasPendingAncLevelChange =>
        _pendingAncLevelValue.HasValue &&
        _pendingAncLevelValue.Value != (CurrentDevice.AncLevel ?? 0);

    public string CurrentAncLevelText {
        get {
            var value = CurrentAncLevelValue;
            return HasPendingAncLevelChange
                ? $"{value} pending"
                : CurrentDevice.AncLevel?.ToString() ?? "Unknown";
        }
    }

    public ButtonFunctionOption? SelectedLeftSingleTapButtonFunction {
        get => FindButtonFunctionOption(SingleTapButtonOptions, CurrentTouchButtonDraft.LeftSingleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.SingleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightSingleTapButtonFunction {
        get => FindButtonFunctionOption(SingleTapButtonOptions, CurrentTouchButtonDraft.RightSingleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.SingleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftDoubleTapButtonFunction {
        get => FindButtonFunctionOption(DoubleTapButtonOptions, CurrentTouchButtonDraft.LeftDoubleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.DoubleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightDoubleTapButtonFunction {
        get => FindButtonFunctionOption(DoubleTapButtonOptions, CurrentTouchButtonDraft.RightDoubleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.DoubleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftTripleTapButtonFunction {
        get => FindButtonFunctionOption(TripleTapButtonOptions, CurrentTouchButtonDraft.LeftTripleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.TripleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightTripleTapButtonFunction {
        get => FindButtonFunctionOption(TripleTapButtonOptions, CurrentTouchButtonDraft.RightTripleTap);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.TripleTap, value?.Value);
    }

    public ButtonFunctionOption? SelectedLeftTapAndHoldButtonFunction {
        get => FindButtonFunctionOption(TapAndHoldButtonOptions, CurrentTouchButtonDraft.LeftTapAndHold);
        set => SetTouchButtonBinding(NuraButtonSide.Left, NuraButtonGesture.TapAndHold, value?.Value);
    }

    public ButtonFunctionOption? SelectedRightTapAndHoldButtonFunction {
        get => FindButtonFunctionOption(TapAndHoldButtonOptions, CurrentTouchButtonDraft.RightTapAndHold);
        set => SetTouchButtonBinding(NuraButtonSide.Right, NuraButtonGesture.TapAndHold, value?.Value);
    }

    public DialFunctionOption? SelectedLeftDialFunction {
        get => FindDialFunctionOption(CurrentDialDraft.Left);
        set => SetDialFunction(NuraDialSide.Left, value?.Value ?? NuraDialFunction.None);
    }

    public DialFunctionOption? SelectedRightDialFunction {
        get => FindDialFunctionOption(CurrentDialDraft.Right);
        set => SetDialFunction(NuraDialSide.Right, value?.Value ?? NuraDialFunction.None);
    }

    public bool HasPendingTouchButtonChanges =>
        _pendingTouchButtons is not null &&
        !Equals(_pendingTouchButtons, CurrentDevice.TouchButtons ?? new NuraButtonConfiguration());

    public bool HasPendingDialChanges =>
        _pendingDial is not null &&
        !Equals(_pendingDial, CurrentDevice.Dial ?? new NuraDialConfiguration());

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
        OnPropertyChanged(nameof(ShouldBlurCurrentDeviceControls));
        OnPropertyChanged(nameof(ShowCurrentDeviceActionPanel));
        OnPropertyChanged(nameof(CurrentDeviceActionText));
        OnPropertyChanged(nameof(CurrentDeviceReadinessText));
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
        OnPropertyChanged(nameof(HasPendingImmersionChange));
        OnPropertyChanged(nameof(HasPendingAncLevelChange));
        OnPropertyChanged(nameof(HasPendingTouchButtonChanges));
        OnPropertyChanged(nameof(HasPendingDialChanges));

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
        OnPropertyChanged(nameof(HasPendingTouchButtonChanges));
        OnPropertyChanged(nameof(HasPendingDialChanges));
    }

    private NuraButtonConfiguration CurrentTouchButtonDraft =>
        _pendingTouchButtons ?? CurrentDevice.TouchButtons ?? new NuraButtonConfiguration();

    private NuraDialConfiguration CurrentDialDraft =>
        _pendingDial ?? CurrentDevice.Dial ?? new NuraDialConfiguration();

    private async Task ApplyPersonalisationModeAsync(bool isPersonalised) {
        if (CurrentDevice.IsPersonalised == isPersonalised) {
            return;
        }

        await CurrentDevice.ApplyPersonalisationAsync(isPersonalised);
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsPersonalised));
        StartProfileAnimation(profileChanged: false, modeChanged: true);
        RefreshCurrentDeviceBindings();
    }

    private async Task ApplyCurrentProfileSelectionAsync(int profileId) {
        await CurrentDevice.SelectProfileByIndexAsync(profileId);
        RefreshCurrentDeviceBindings();
    }

    private async Task ApplyImmersionAsync() {
        var targetIndex = ImmersionIndex;
        await CurrentDevice.ApplyImmersionIndexAsync(targetIndex);
        _pendingImmersionIndex = null;
        OnPropertyChanged(nameof(ImmersionIndex));
        OnPropertyChanged(nameof(CurrentImmersionValue));
        OnPropertyChanged(nameof(CurrentVisualImmersionValue));
        OnPropertyChanged(nameof(HasPendingImmersionChange));
        UpdateProfileImage();
    }

    private async Task ToggleAncAsync() {
        await CurrentDevice.ApplyAncEnabledAsync(!CurrentDevice.AncEnabled);
        RefreshCurrentDeviceBindings();
    }

    private async Task TogglePassthroughAsync() {
        await CurrentDevice.ApplyPassthroughEnabledAsync(!CurrentDevice.SocialMode);
        RefreshCurrentDeviceBindings();
    }

    private async Task ToggleSpatialAsync() {
        await CurrentDevice.ApplySpatialEnabledAsync(!CurrentDevice.SpatialEnabled);
        RefreshCurrentDeviceBindings();
    }

    private async Task ApplyAncLevelAsync() {
        await CurrentDevice.ApplyAncLevelAsync(CurrentAncLevelValue);
        _pendingAncLevelValue = null;
        OnPropertyChanged(nameof(CurrentAncLevelValue));
        OnPropertyChanged(nameof(CurrentAncLevelText));
        OnPropertyChanged(nameof(HasPendingAncLevelChange));
    }

    private async Task ApplyTouchButtonsAsync() {
        if (!HasPendingTouchButtonChanges) {
            return;
        }

        await CurrentDevice.ApplyTouchButtonsAsync(CurrentTouchButtonDraft);
        _pendingTouchButtons = null;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private async Task ApplyDialAsync() {
        if (!HasPendingDialChanges) {
            return;
        }

        await CurrentDevice.ApplyDialAsync(CurrentDialDraft);
        _pendingDial = null;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void ResetTouchButtonDraft() {
        _pendingTouchButtons = null;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void ResetDialDraft() {
        _pendingDial = null;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void ResetPendingDeviceEdits() {
        _pendingImmersionIndex = null;
        _pendingAncLevelValue = null;
        _pendingTouchButtons = null;
        _pendingDial = null;
        RefreshCurrentDeviceBindings();
    }

    private static int ImmersionValueFromIndex(int immersionIndex) =>
        new[] { -2, -1, 0, 1, 2, 3, 4 }[Math.Clamp(immersionIndex, 0, 6)];

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

        var currentConfiguration = CurrentTouchButtonDraft;
        var nextConfiguration = currentConfiguration.WithBinding(side, gesture, function ?? NuraButtonFunction.None);
        if (Equals(currentConfiguration, nextConfiguration)) {
            return;
        }

        _pendingTouchButtons = nextConfiguration;
        RaiseInteractionSelectionPropertiesChanged();
    }

    private void SetDialFunction(NuraDialSide side, NuraDialFunction function) {
        if (!CurrentDevice.SupportsDial) {
            return;
        }

        var currentConfiguration = CurrentDialDraft;
        var nextConfiguration = currentConfiguration.WithBinding(side, function);
        if (Equals(currentConfiguration, nextConfiguration)) {
            return;
        }

        _pendingDial = nextConfiguration;
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
