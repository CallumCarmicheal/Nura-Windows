# Android Visualisation Fixture

`nura-reference-profile-256.png` is the expected Android output for the synthetic profile in
`ProfileVisualisationTests.CreateSyntheticProfile()`.

Generate it against the 4.5.4 debug APK with:

```powershell
frida -H 127.0.0.1:27042 -n Gadget -l "tools/CaptureVisualisationFixture.js"
```

The script prints the app-private output path. Pull that file with `adb` into this directory as
`nura-reference-profile-256.png`. Once present, `NuraLib.Tests` compares every rendered pixel
against it with strict mean-error and high-error-ratio thresholds.
