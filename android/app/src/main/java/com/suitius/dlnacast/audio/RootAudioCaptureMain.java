package com.suitius.dlnacast.audio;

import android.annotation.SuppressLint;
import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.os.Looper;
import android.os.Process;
import android.util.Log;

import java.io.BufferedOutputStream;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.net.InetAddress;
import java.net.Socket;
import java.net.SocketException;

/** Entry point executed by app_process as uid 0. */
public final class RootAudioCaptureMain {
    private static final String TAG = "DLNACast.RootCapture";
    private static final int SAMPLE_RATE = 48_000;
    private static final int CHANNELS = AudioFormat.CHANNEL_IN_STEREO;
    private static final int ENCODING = AudioFormat.ENCODING_PCM_16BIT;
    private static final int AUDIO_MANAGER_SUCCESS = 0;

    private RootAudioCaptureMain() {}

    @SuppressLint({"MissingPermission", "WrongConstant"})
    public static void main(String[] args) throws Exception {
        if (args.length != 2) {
            throw new IllegalArgumentException("expected loopback host and port");
        }
        if (Process.myUid() != 0) {
            throw new SecurityException("root audio helper is not uid 0");
        }

        int port = Integer.parseInt(args[1]);
        int minimumBuffer = AudioRecord.getMinBufferSize(SAMPLE_RATE, CHANNELS, ENCODING);
        if (minimumBuffer <= 0) {
            throw new IllegalStateException("AudioRecord minimum buffer failed: " + minimumBuffer);
        }
        int bufferSize = Math.max(minimumBuffer, 48_000);
        AudioFormat format = new AudioFormat.Builder()
                .setEncoding(ENCODING)
                .setSampleRate(SAMPLE_RATE)
                .setChannelMask(CHANNELS)
                .build();
        RootAudioLoopback loopback = RootAudioLoopback.open(format);
        AudioRecord record = loopback.record;
        if (record.getState() != AudioRecord.STATE_INITIALIZED) {
            loopback.close();
            throw new IllegalStateException("root AudioPolicy loopback could not be initialized");
        }

        Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO);
        // AudioRecord ring capacity is not the read size: 48,000 bytes batches 250 ms.
        // Match Windows PcmFrameFormat.MiPlayAac's 1024-sample input frames.
        byte[] pcm = new byte[PcmFrameBuffer.MIPLAY_FRAME_BYTES];
        try {
            try (Socket socket = new Socket(InetAddress.getByName(args[0]), port);
                 BufferedOutputStream output = new BufferedOutputStream(socket.getOutputStream(), bufferSize * 2)) {
                socket.setTcpNoDelay(true);
                record.startRecording();
                Log.i(TAG, "uid=0 AudioPolicy playback loopback started on "
                        + SAMPLE_RATE + " Hz stereo PCM16");
                while (!Thread.currentThread().isInterrupted()) {
                    int read = record.read(pcm, 0, pcm.length, AudioRecord.READ_BLOCKING);
                    if (read > 0) {
                        output.write(pcm, 0, read);
                        output.flush();
                    } else if (read == AudioRecord.ERROR_DEAD_OBJECT) {
                        throw new IllegalStateException("root AudioRecord died");
                    } else if (read < 0) {
                        throw new IllegalStateException("root AudioRecord read failed: " + read);
                    }
                }
            }
        } catch (SocketException peerClosed) {
            Log.i(TAG, "Root capture peer closed: " + peerClosed.getMessage());
        } finally {
            try {
                record.stop();
            } catch (RuntimeException ignored) {
            }
            loopback.close();
        }
    }

    private static final class RootAudioLoopback implements AutoCloseable {
        private final AudioManager audioManager;
        private final Object audioPolicy;
        private final AudioRecord record;

        private RootAudioLoopback(
                AudioManager audioManager,
                Object audioPolicy,
                AudioRecord record) {
            this.audioManager = audioManager;
            this.audioPolicy = audioPolicy;
            this.record = record;
        }

        static RootAudioLoopback open(AudioFormat format) throws Exception {
            Context context = systemContext();
            AudioManager audioManager = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            if (audioManager == null) throw new IllegalStateException("AudioManager is unavailable");

            Class<?> ruleClass = Class.forName("android.media.audiopolicy.AudioMixingRule");
            Class<?> ruleBuilderClass = Class.forName(
                    "android.media.audiopolicy.AudioMixingRule$Builder");
            Object ruleBuilder = declaredConstructor(ruleBuilderClass).newInstance();
            int matchUsage = staticInt(ruleClass, "RULE_MATCH_ATTRIBUTE_USAGE");
            Method addRule = declaredMethod(
                    ruleBuilderClass,
                    "addRule",
                    AudioAttributes.class,
                    int.class);
            int[] usages = {
                    AudioAttributes.USAGE_UNKNOWN,
                    AudioAttributes.USAGE_MEDIA,
                    AudioAttributes.USAGE_GAME,
                    AudioAttributes.USAGE_ASSISTANCE_NAVIGATION_GUIDANCE,
                    AudioAttributes.USAGE_ASSISTANCE_SONIFICATION,
                    AudioAttributes.USAGE_ASSISTANT,
            };
            for (int usage : usages) {
                AudioAttributes attributes = new AudioAttributes.Builder()
                        .setUsage(usage)
                        .build();
                addRule.invoke(ruleBuilder, attributes, matchUsage);
            }
            Object rule = declaredMethod(ruleBuilderClass, "build").invoke(ruleBuilder);

            Class<?> mixClass = Class.forName("android.media.audiopolicy.AudioMix");
            Class<?> mixBuilderClass = Class.forName("android.media.audiopolicy.AudioMix$Builder");
            Object mixBuilder = declaredConstructor(mixBuilderClass, ruleClass).newInstance(rule);
            declaredMethod(mixBuilderClass, "setFormat", AudioFormat.class)
                    .invoke(mixBuilder, format);
            declaredMethod(mixBuilderClass, "setRouteFlags", int.class)
                    .invoke(mixBuilder, staticInt(mixClass, "ROUTE_FLAG_LOOP_BACK"));
            Object mix = declaredMethod(mixBuilderClass, "build").invoke(mixBuilder);

            Class<?> policyClass = Class.forName("android.media.audiopolicy.AudioPolicy");
            Class<?> policyBuilderClass = Class.forName("android.media.audiopolicy.AudioPolicy$Builder");
            Object policyBuilder = declaredConstructor(policyBuilderClass, Context.class)
                    .newInstance(context);
            declaredMethod(policyBuilderClass, "addMix", mixClass).invoke(policyBuilder, mix);
            Object policy = declaredMethod(policyBuilderClass, "build").invoke(policyBuilder);

            int status = (Integer) declaredMethod(
                    AudioManager.class,
                    "registerAudioPolicy",
                    policyClass).invoke(audioManager, policy);
            if (status != AUDIO_MANAGER_SUCCESS) {
                throw new IllegalStateException("AudioPolicy registration failed: " + status);
            }
            try {
                AudioRecord record = (AudioRecord) declaredMethod(
                        policyClass,
                        "createAudioRecordSink",
                        mixClass).invoke(policy, mix);
                if (record == null) throw new IllegalStateException("AudioPolicy returned no loopback recorder");
                return new RootAudioLoopback(audioManager, policy, record);
            } catch (Exception error) {
                unregister(audioManager, policyClass, policy);
                throw error;
            }
        }

        @Override
        public void close() {
            record.release();
            unregister(audioManager, audioPolicy.getClass(), audioPolicy);
        }

        private static Context systemContext() throws Exception {
            if (Looper.myLooper() == null) Looper.prepareMainLooper();
            Class<?> activityThreadClass = Class.forName("android.app.ActivityThread");
            Object activityThread = declaredMethod(activityThreadClass, "currentActivityThread")
                    .invoke(null);
            if (activityThread == null) {
                activityThread = declaredMethod(activityThreadClass, "systemMain").invoke(null);
            }
            Object context = declaredMethod(activityThreadClass, "getSystemContext")
                    .invoke(activityThread);
            if (!(context instanceof Context)) {
                throw new IllegalStateException("Android system context is unavailable");
            }
            return (Context) context;
        }

        private static void unregister(
                AudioManager audioManager,
                Class<?> policyClass,
                Object policy) {
            try {
                declaredMethod(AudioManager.class, "unregisterAudioPolicy", policyClass)
                        .invoke(audioManager, policy);
            } catch (Throwable error) {
                Log.w(TAG, "Unable to unregister root AudioPolicy", error);
            }
        }

        private static Constructor<?> declaredConstructor(
                Class<?> type,
                Class<?>... parameterTypes) throws NoSuchMethodException {
            Constructor<?> constructor = type.getDeclaredConstructor(parameterTypes);
            constructor.setAccessible(true);
            return constructor;
        }

        private static Method declaredMethod(
                Class<?> type,
                String name,
                Class<?>... parameterTypes) throws NoSuchMethodException {
            Method method = type.getDeclaredMethod(name, parameterTypes);
            method.setAccessible(true);
            return method;
        }

        private static int staticInt(Class<?> type, String name) throws Exception {
            Field field = type.getDeclaredField(name);
            field.setAccessible(true);
            return field.getInt(null);
        }
    }
}
