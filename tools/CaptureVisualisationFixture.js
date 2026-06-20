'use strict';

// Captures the Android native static renderer for the synthetic test profile.
// Run against the debug APK, then pull the path printed by the script with adb.
setImmediate(function () {
    Java.perform(function () {
        var ActivityThread = Java.use('android.app.ActivityThread');
        var NuraVisualisationWithoutView = Java.use(
            'com.nuraphone.android.nuravisualisation.NuraVisualisationWithoutView');
        var BitmapCompressFormat = Java.use('android.graphics.Bitmap$CompressFormat');
        var File = Java.use('java.io.File');
        var FileOutputStream = Java.use('java.io.FileOutputStream');

        var context = ActivityThread.currentApplication().getApplicationContext();
        var directory = context.getExternalFilesDir.overload('java.lang.String').call(context, null);
        var output = File.$new(directory, 'nura-reference-profile-256.png');
        var left = Java.array('float', [-8, -4, 0, 5, 10, 2, -6, -12, -4, 3, 8, 4]);
        var right = Java.array('float', [-6, -2, 2, 7, 8, 0, -8, -10, -2, 5, 6, 2]);
        var renderer = NuraVisualisationWithoutView.$new(256);

        try {
            renderer.setFrequencyColourValue(0.35);
            renderer.setData(left, right);

            var bitmap = renderer.getBitmap();
            var stream = FileOutputStream.$new(output);
            try {
                if (!bitmap.compress(BitmapCompressFormat.PNG.value, 100, stream)) {
                    throw new Error('Bitmap.compress returned false');
                }
            } finally {
                stream.close();
            }

            var message = '[NURA_REFERENCE] path=' + output.getAbsolutePath();
            console.log(message);
            send({ type: 'nura-reference-profile', path: output.getAbsolutePath() });
        } finally {
            renderer.cleanup();
        }
    });
});
