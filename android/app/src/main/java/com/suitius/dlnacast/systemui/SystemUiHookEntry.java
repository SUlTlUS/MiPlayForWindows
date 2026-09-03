package com.suitius.dlnacast.systemui;

import android.content.ComponentName;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.content.ServiceConnection;
import android.content.res.ColorStateList;
import android.database.Cursor;
import android.database.ContentObserver;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.TextView;

import java.lang.ref.WeakReference;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

import io.github.libxposed.api.XposedInterface;
import io.github.libxposed.api.XposedModule;
import io.github.libxposed.api.XposedModuleInterface;

public final class SystemUiHookEntry extends XposedModule {
    private static final String TAG = "MiPlaySystemUI";
    private static final String SYSTEM_UI_PACKAGE = "com.android.systemui";
    private static final String APP_PACKAGE = "com.suitius.dlnacast";
    private static final String APP_ACTIVITY = "com.suitius.dlnacast.MainActivity";
    private static final String BRIDGE_SERVICE =
            "com.suitius.dlnacast.systemui.SystemUiBridgeService";
    private static final String ACTION_SELECTION =
            "com.suitius.dlnacast.action.SYSTEM_UI_SELECTION";
    private static final String EXTRA_NONCE = "system_ui_selection_nonce";
    private static final String EXTRA_RENDERER_ID = "system_ui_renderer_id";
    private static final String EXTRA_RENDERER_NAME = "system_ui_renderer_name";
    private static final String EXTRA_RENDERER_HOST = "system_ui_renderer_host";
    private static final String CONTROL_RECEIVER =
            "com.suitius.dlnacast.systemui.SystemUiControlReceiver";
    private static final String ACTION_ADD_RENDERER =
            "com.suitius.dlnacast.action.SYSTEM_UI_ADD_RENDERER";
    private static final String ACTION_ROOT_START =
            "com.suitius.dlnacast.action.SYSTEM_UI_ROOT_START";
    private static final String ACTION_REMOVE_RENDERER =
            "com.suitius.dlnacast.action.SYSTEM_UI_REMOVE_RENDERER";
    private static final String ACTION_SET_VOLUME =
            "com.suitius.dlnacast.action.SYSTEM_UI_SET_VOLUME";
    private static final String ACTION_STOP =
            "com.suitius.dlnacast.action.SYSTEM_UI_STOP";
    private static final String EXTRA_VOLUME = "system_ui_volume";
    private static final String EXTRA_RECEIVER_INDEX = "system_ui_receiver_index";
    private static final int OPLUS_VOLUME_SCALE = 1_000;
    private static final Uri SPEAKERS_URI =
            Uri.parse("content://com.suitius.dlnacast.systemui/speakers");
    private static final String METHOD_PREPARE_SELECTION = "prepareSelection";
    private static final String METHOD_STATUS = "status";
    private static final String METHOD_STOP = "stop";
    private static final String KEY_NONCE = "nonce";
    private static final String KEY_ERROR = "error";
    private static final String KEY_STATE = "state";
    private static final String KEY_ENABLED = "enabled";
    private static final String KEY_RENDERER_IDS = "rendererIds";
    private static final String KEY_RENDERER_HOSTS = "rendererHosts";

    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static final ExecutorService DISCOVERY_EXECUTOR = Executors.newSingleThreadExecutor();
    private static final int DISCOVERY_RETRY_LIMIT = 3;
    private static final long DISCOVERY_RETRY_DELAY_MILLISECONDS = 1_200L;
    private static final long PREPARING_REFRESH_DELAY_MILLISECONDS = 1_000L;
    private static final long PREPARING_TIMEOUT_MILLISECONDS = 20_000L;
    private static final int MAX_MIPLAY_RECEIVERS = 63;
    private static final AtomicBoolean PREPARING_REFRESH_SCHEDULED = new AtomicBoolean();
    private static final AtomicBoolean SPEAKER_REFRESH_IN_FLIGHT = new AtomicBoolean();
    private static final AtomicBoolean STATUS_REFRESH_QUEUED = new AtomicBoolean();
    private static volatile long preparingDeadlineElapsedRealtime;
    private static final CopyOnWriteArrayList<WeakReference<Object>> ADAPTERS =
            new CopyOnWriteArrayList<>();
    private static final CopyOnWriteArrayList<String> SELECTED_ORDER =
            new CopyOnWriteArrayList<>();
    private static volatile List<Speaker> speakers = Collections.emptyList();
    private static volatile boolean systemUiEnabled = true;
    private static volatile SystemUiHookEntry instance;
    private static volatile boolean itemCountLogged;
    private static volatile Drawable.ConstantState phoneItemBackgroundState;
    private static volatile Drawable.ConstantState phoneIconBackgroundState;
    private static volatile ColorStateList phoneTitleColors;
    private static volatile WeakReference<Object> visibleOutputPage = new WeakReference<>(null);
    private static Context bridgeBoundContext;
    private static ServiceConnection bridgeConnection;
    private static ContentObserver statusObserver;
    private boolean installed;

    @Override
    public void onModuleLoaded(XposedModuleInterface.ModuleLoadedParam param) {
        instance = this;
        log("module loaded in " + param.getProcessName() + ", API " + getApiVersion());
    }

    @Override
    public synchronized void onPackageReady(XposedModuleInterface.PackageReadyParam param) {
        if (installed || !SYSTEM_UI_PACKAGE.equals(param.getPackageName())) return;
        try {
            install(param.getClassLoader());
            installed = true;
            log("installed for Android 16 media output dialog");
        } catch (Throwable error) {
            log("installation failed: " + error);
        }
    }

    private void install(ClassLoader classLoader) throws ClassNotFoundException {
        Class<?> adapterBase = Class.forName(
                "com.android.systemui.media.dialog.MediaOutputAdapterBase",
                false,
                classLoader);
        Class<?> oplusAdapter = Class.forName(
                "com.oplus.systemui.media.output.OplusMediaOutputAdapter",
                false,
                classLoader);
        Class<?> oplusSwitchingPage = Class.forName(
                "com.oplus.systemui.media.output.OplusMediaOutputSwitchingPage",
                false,
                classLoader);

        hookAllMethods(adapterBase, "getItemCount", chain -> {
            Object result = chain.proceed();
            try {
                if (isOplusAdapter(chain.getThisObject()) && result instanceof Integer) {
                    rememberAdapter(chain.getThisObject());
                    int extraCount = speakerSnapshot().size();
                    if (!itemCountLogged) {
                        itemCountLogged = true;
                        log("ColorOS adapter count " + result + " + " + extraCount + " MiPlay");
                    }
                    return ((Integer) result) + extraCount;
                }
            } catch (Throwable error) {
                log("getItemCount hook failed: " + error);
            }
            return result;
        });

        hookAllMethods(adapterBase, "getItemViewType", chain -> {
            try {
                Object adapter = chain.getThisObject();
                int position = (Integer) chain.getArg(0);
                if (isOplusAdapter(adapter) && position >= baseItemCount(adapter)) return 0;
            } catch (Throwable error) {
                log("getItemViewType hook failed: " + error);
            }
            return chain.proceed();
        });

        hookAllMethods(oplusAdapter, "onBindViewHolder", chain -> {
            Object adapter = chain.getThisObject();
            Object holder = chain.getArg(0);
            int position = (Integer) chain.getArg(1);
            int baseCount;
            try {
                baseCount = baseItemCount(adapter);
                if (position >= baseCount) {
                    List<Speaker> snapshot = speakerSnapshot();
                    int speakerIndex = position - baseCount;
                    if (speakerIndex >= 0 && speakerIndex < snapshot.size()) {
                        bindOplusSpeakerRow(holder, snapshot.get(speakerIndex));
                        log("bound ColorOS MiPlay row " + speakerIndex + ": "
                                + snapshot.get(speakerIndex).name);
                    }
                    return null;
                }
            } catch (Throwable error) {
                log("speaker row bind failed: " + error);
                return null;
            }
            restoreOplusSeekBarLayout(holder);
            Object result = chain.proceed();
            try {
                // The native row is bound before the appended MiPlay rows. Capture its
                // actual drawable immediately so the custom rows use the same colors on
                // their very first frame instead of waiting for media-device detection.
                if (position >= 0 && position < baseCount) {
                    captureOplusPhoneRowStyle(holder);
                }
                if (isPhoneMediaItem(adapter, position)) {
                    if (hasSelectedSpeaker()) bindOplusPhoneReturnRow(holder);
                }
            } catch (Throwable error) {
                log("phone row hook failed: " + error);
            }
            return result;
        });

        hookAllMethods(oplusSwitchingPage, "onShow", chain -> {
            Object result = chain.proceed();
            try {
                Object page = chain.getThisObject();
                Context context = (Context) invokeNoArgs(page, "getContext");
                Object adapter = findFieldValue(page, "adapter");
                rememberAdapter(adapter);
                configureTopVolume(page, context);
                visibleOutputPage = new WeakReference<>(page);
                bindOutputBridge(context);
            } catch (Throwable error) {
                log("ColorOS output page hook failed: " + error);
            }
            return result;
        });

        hookAllMethods(oplusSwitchingPage, "onDismiss", chain -> {
            try {
                return chain.proceed();
            } finally {
                if (visibleOutputPage.get() == chain.getThisObject()) {
                    visibleOutputPage.clear();
                    unbindOutputBridge();
                    ADAPTERS.clear();
                }
            }
        });
    }

    private static void bindOutputBridge(Context context) {
        Context appContext = context.getApplicationContext();
        if (appContext == null) appContext = context;
        final Context bindingContext = appContext;
        if (bridgeConnection != null) {
            refreshSpeakers(bindingContext);
            return;
        }
        ServiceConnection connection = new ServiceConnection() {
            @Override
            public void onServiceConnected(ComponentName name, IBinder service) {
                if (bridgeConnection != this || visibleOutputPage.get() == null) return;
                log("output bridge connected; querying live MiPlay speakers");
                observeCastStatus(bindingContext);
                refreshSpeakers(bindingContext);
            }

            @Override
            public void onServiceDisconnected(ComponentName name) {
                log("output bridge disconnected; waiting for service reconnection");
            }

            @Override
            public void onBindingDied(ComponentName name) {
                if (bridgeConnection != this) return;
                log("output bridge binding died");
                unbindOutputBridge();
            }

            @Override
            public void onNullBinding(ComponentName name) {
                if (bridgeConnection != this) return;
                log("output bridge returned no binder");
                unbindOutputBridge();
            }
        };
        bridgeConnection = connection;
        bridgeBoundContext = bindingContext;
        try {
            boolean bound = bindingContext.bindService(
                    new Intent().setComponent(new ComponentName(APP_PACKAGE, BRIDGE_SERVICE)),
                    connection,
                    Context.BIND_AUTO_CREATE | Context.BIND_IMPORTANT);
            log("output bridge bind requested: " + bound);
            if (!bound) {
                unbindOutputBridge();
                refreshSpeakers(bindingContext);
            }
        } catch (Throwable error) {
            log("output bridge bind failed: " + error);
            unbindOutputBridge();
            refreshSpeakers(bindingContext);
        }
    }

    private static void unbindOutputBridge() {
        ServiceConnection connection = bridgeConnection;
        Context context = bridgeBoundContext;
        bridgeConnection = null;
        bridgeBoundContext = null;
        ContentObserver observer = statusObserver;
        statusObserver = null;
        if (observer != null && context != null) {
            context.getContentResolver().unregisterContentObserver(observer);
        }
        if (connection == null || context == null) return;
        try {
            context.unbindService(connection);
            log("output bridge released");
        } catch (IllegalArgumentException ignored) {
            // A failed bind has no connection to release.
        }
    }

    private static void observeCastStatus(Context context) {
        if (statusObserver != null) return;
        ContentObserver observer = new ContentObserver(MAIN_HANDLER) {
            @Override
            public void onChange(boolean selfChange) {
                if (statusObserver == this) refreshSpeakerStatus(context);
            }
        };
        try {
            context.getContentResolver().registerContentObserver(SPEAKERS_URI, false, observer);
            statusObserver = observer;
            log("observing live cast selection changes");
        } catch (Throwable error) {
            log("cast state observer failed: " + error);
        }
    }

    private static void refreshSpeakerStatus(Context context) {
        if (visibleOutputPage.get() == null || !STATUS_REFRESH_QUEUED.compareAndSet(false, true)) return;
        DISCOVERY_EXECUTOR.execute(() -> {
            STATUS_REFRESH_QUEUED.set(false);
            if (visibleOutputPage.get() == null) return;
            boolean wasEnabled = systemUiEnabled;
            boolean changed = applyProviderStatus(context);
            if (changed) MAIN_HANDLER.post(SystemUiHookEntry::notifyAdapters);
            if (!wasEnabled && systemUiEnabled) refreshSpeakers(context);
            updatePreparingRefresh(context);
        });
    }

    private void hookAllMethods(
            Class<?> type,
            String methodName,
            XposedInterface.Hooker hooker) {
        int hooked = 0;
        for (Method method : type.getDeclaredMethods()) {
            if (!methodName.equals(method.getName())) continue;
            method.setAccessible(true);
            hook(method)
                    .setExceptionMode(XposedInterface.ExceptionMode.PROTECTIVE)
                    .intercept(hooker);
            hooked++;
        }
        log("hooked " + type.getSimpleName() + "." + methodName + " x" + hooked);
    }

    private static void bindOplusSpeakerRow(Object holder, Speaker speaker)
            throws ReflectiveOperationException {
        Context context = (Context) findFieldValue(holder, "context");
        FrameLayout container = (FrameLayout) findFieldValue(holder, "mainContent");
        View root = (View) findFieldValue(holder, "view");
        TextView title = (TextView) findFieldValue(holder, "titleText");
        TextView subtitle = (TextView) findFieldValue(holder, "subTitleText");
        ImageView titleIcon = (ImageView) findFieldValue(holder, "titleIcon");
        View iconArea = (View) findFieldValue(holder, "iconAreaLayout");
        View progress = (View) findFieldValue(holder, "progressBarParent");
        FrameLayout checkBoxFrame = (FrameLayout) findFieldValue(holder, "checkBoxFrame");
        ImageView checkBox = (ImageView) findFieldValue(holder, "checkBox");
        Object seekBarValue = findFieldValue(holder, "seekBar");
        View vendorSeekBar = seekBarValue instanceof View ? (View) seekBarValue : null;

        root.setVisibility(View.VISIBLE);
        clearViewBackground(root);
        Object itemViewValue = findFieldValue(holder, "itemView");
        if (itemViewValue instanceof View) clearViewBackground((View) itemViewValue);
        title.setVisibility(View.VISIBLE);
        title.setText(speaker.name);
        subtitle.setVisibility(View.GONE);
        boolean perSpeakerVolume = speaker.selected && selectedSpeakerCount() > 1;
        int backgroundColor = speaker.selected
                ? Color.rgb(52, 127, 246)
                : Color.argb(46, 255, 255, 255);
        int contentColor = Color.luminance(backgroundColor) < 0.45f
                ? Color.WHITE
                : Color.rgb(24, 24, 24);
        if (phoneTitleColors != null) {
            title.setTextColor(phoneTitleColors);
            contentColor = phoneTitleColors.getDefaultColor();
        } else {
            title.setTextColor(contentColor);
        }
        subtitle.setTextColor(contentColor);

        boolean connecting = "preparing".equals(speaker.state);
        progress.setVisibility(connecting ? View.VISIBLE : View.GONE);
        titleIcon.setVisibility(View.VISIBLE);
        int iconId = context.getResources().getIdentifier(
                "ic_media_speaker_device",
                "drawable",
                SYSTEM_UI_PACKAGE);
        if (iconId != 0) titleIcon.setImageResource(iconId);
        titleIcon.setImageTintList(ColorStateList.valueOf(contentColor));

        checkBoxFrame.setVisibility(connecting ? View.GONE : View.VISIBLE);
        int checkDrawable = context.getResources().getIdentifier(
                speaker.selected
                        ? "qs_media_output_item_check_box_connected"
                        : "qs_media_output_item_check_box",
                "drawable",
                SYSTEM_UI_PACKAGE);
        if (checkDrawable != 0) checkBox.setImageResource(checkDrawable);
        checkBox.setSelected(speaker.selected);
        checkBox.setActivated(speaker.selected);
        checkBox.setVisibility(View.VISIBLE);

        configureSpeakerVolume(
                context, container, root, vendorSeekBar, speaker, perSpeakerVolume);
        configureOplusTouchRouter(container, vendorSeekBar, iconArea, checkBoxFrame);
        applyPhoneRowStyle(
                holder,
                context,
                container,
                iconArea,
                backgroundColor,
                speaker.selected,
                perSpeakerVolume);
        iconArea.setVisibility(View.VISIBLE);
        iconArea.setOnClickListener(null);
        container.setClipToOutline(true);
        container.setTag(speaker.selected ? "connected" : null);
        container.setContentDescription(speaker.name + ", " + speakerSubtitle(speaker));
        if (perSpeakerVolume) {
            container.setFocusable(false);
            container.setEnabled(true);
            container.setClickable(true);
            container.setOnClickListener(null);
            iconArea.setClickable(false);
            checkBoxFrame.setClickable(true);
            checkBoxFrame.setOnClickListener(view -> toggleSpeaker(context, speaker));
        } else {
            container.setFocusable(true);
            container.setClickable(true);
            container.setOnClickListener(view -> toggleSpeaker(context, speaker));
            iconArea.setClickable(false);
            checkBoxFrame.setClickable(false);
            checkBoxFrame.setOnClickListener(null);
        }
    }

    private static void configureSpeakerVolume(
            Context context,
            View container,
            View root,
            View vendorSeekBar,
            Speaker speaker,
            boolean visible) throws ReflectiveOperationException {
        if (vendorSeekBar != null) vendorSeekBar.setVisibility(View.GONE);
        setViewHeight(context, root, 48);
        setViewHeight(context, container, 48);
        if (vendorSeekBar == null) return;
        if (!visible) {
            setNativeSeekBarListener(vendorSeekBar, null);
            vendorSeekBar.setOnTouchListener(null);
            vendorSeekBar.setVisibility(View.GONE);
            return;
        }
        vendorSeekBar.setEnabled(true);
        vendorSeekBar.setClickable(true);
        vendorSeekBar.setVisibility(View.VISIBLE);
        FrameLayout.LayoutParams sliderParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT);
        sliderParams.gravity = Gravity.CENTER;
        vendorSeekBar.setLayoutParams(sliderParams);
        configureNativeSeekBar(vendorSeekBar, normalizedVolume(speaker), new VolumeListener() {
            @Override
            public void onChanged(int volume) {
                updateSpeakerVolume(speaker.id, volume);
            }

            @Override
            public void onStopped(int volume) {
                sendVolume(context, volume, selectedSpeakerIndex(speaker.id));
            }
        });
        installDirectVolumeTouchHandler(context, vendorSeekBar, speaker);
    }

    private static void configureOplusTouchRouter(
            View container,
            View vendorSeekBar,
            View iconArea,
            FrameLayout checkBoxFrame) throws ReflectiveOperationException {
        if (vendorSeekBar == null
                || !"com.oplus.systemui.media.output.OplusMediaOutputItemLayout"
                .equals(container.getClass().getName())) {
            return;
        }
        setFieldValue(container, "seekBar", vendorSeekBar);
        setFieldValue(container, "iconView", iconArea);
        setFieldValue(container, "checkBox", checkBoxFrame);
        setFieldValue(container, "isDragging", false);
        setFieldValue(container, "isDownOnSeekbar", false);
        try {
            setFieldValue(vendorSeekBar, "isEventCancel", false);
        } catch (NoSuchFieldException ignored) {
        }
    }

    private static void installDirectVolumeTouchHandler(
            Context context,
            View vendorSeekBar,
            Speaker speaker) {
        vendorSeekBar.setOnTouchListener((view, event) -> {
            int action = event.getActionMasked();
            if (action == MotionEvent.ACTION_CANCEL) return true;
            if (action != MotionEvent.ACTION_DOWN
                    && action != MotionEvent.ACTION_MOVE
                    && action != MotionEvent.ACTION_UP) {
                return false;
            }

            int width = view.getWidth() - view.getPaddingLeft() - view.getPaddingRight();
            if (width <= 0) return true;
            float fraction = (event.getX() - view.getPaddingLeft()) / width;
            fraction = Math.max(0f, Math.min(1f, fraction));
            if (view.getLayoutDirection() == View.LAYOUT_DIRECTION_RTL) {
                fraction = 1f - fraction;
            }
            int volume = clampVolume(Math.round(fraction * 100f));
            try {
                invokeOneIntArg(vendorSeekBar, "setVolume", volume);
            } catch (ReflectiveOperationException error) {
                log("set direct ColorOS volume failed: " + error);
                return true;
            }
            updateSpeakerVolume(speaker.id, volume);
            if (action == MotionEvent.ACTION_UP) {
                view.performClick();
                sendVolume(context, volume, selectedSpeakerIndex(speaker.id));
            }
            return true;
        });
    }

    private static void configureTopVolume(Object page, Context context)
            throws ReflectiveOperationException {
        if (selectedSpeakerCount() == 0) return;
        Object value = findFieldValue(page, "volumeSeekbar");
        if (!(value instanceof View)) return;
        configureNativeSeekBar(value, selectedAverageVolume(), new VolumeListener() {
            @Override
            public void onChanged(int volume) {
                updateSelectedVolumes(volume);
            }

            @Override
            public void onStopped(int volume) {
                sendVolume(context, volume, null);
            }
        });
    }

    private static void restoreOplusSeekBarLayout(Object holder) {
        try {
            Object value = findFieldValue(holder, "seekBar");
            if (!(value instanceof View)) return;
            View seekBar = (View) value;
            FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT);
            seekBar.setLayoutParams(params);
        } catch (Throwable error) {
            log("restore ColorOS seekbar layout failed: " + error);
        }
    }

    private static void configureNativeSeekBar(
            Object seekBar,
            int volume,
            VolumeListener listener) throws ReflectiveOperationException {
        try {
            invokeOneIntArg(seekBar, "setMaxVolume", 100);
        } catch (NoSuchMethodException ignored) {
            invokeOneIntArg(seekBar, "setMax", 100 * OPLUS_VOLUME_SCALE);
        }
        try {
            invokeOneIntArg(seekBar, "setVolume", volume);
        } catch (NoSuchMethodException ignored) {
            invokeOneIntArg(seekBar, "setProgress", volume * OPLUS_VOLUME_SCALE);
        }
        setNativeSeekBarListener(seekBar, listener);
    }

    private static void setNativeSeekBarListener(Object seekBar, VolumeListener listener)
            throws ReflectiveOperationException {
        Method setter = findOneArgMethod(seekBar.getClass(), "setOnSeekBarChangeListener");
        setter.setAccessible(true);
        if (listener == null) {
            setter.invoke(seekBar, new Object[]{null});
            return;
        }
        Class<?> listenerType = setter.getParameterTypes()[0];
        Object proxy = Proxy.newProxyInstance(
                listenerType.getClassLoader(),
                new Class<?>[]{listenerType},
                (ignoredProxy, method, args) -> {
                    String name = method.getName();
                    if ("onProgressChanged".equals(name)
                            && args != null
                            && args.length >= 3
                            && args[1] instanceof Integer
                            && Boolean.TRUE.equals(args[2])) {
                        listener.onChanged(nativeProgressToVolume((Integer) args[1]));
                    } else if ("onStopTrackingTouch".equals(name)) {
                        listener.onStopped(readNativeVolume(seekBar));
                    } else if ("toString".equals(name)) {
                        return "MiPlayVolumeListener";
                    } else if ("hashCode".equals(name)) {
                        return System.identityHashCode(ignoredProxy);
                    } else if ("equals".equals(name)) {
                        return args != null && args.length == 1 && ignoredProxy == args[0];
                    }
                    return null;
                });
        setter.invoke(seekBar, proxy);
    }

    private static int readNativeVolume(Object seekBar) {
        try {
            Object value = invokeNoArgs(seekBar, "getVolume");
            if (value instanceof Integer) return clampVolume((Integer) value);
        } catch (Throwable ignored) {
        }
        try {
            Object value = invokeNoArgs(seekBar, "getProgress");
            if (value instanceof Integer) return nativeProgressToVolume((Integer) value);
        } catch (Throwable ignored) {
        }
        return 0;
    }

    private static int nativeProgressToVolume(int progress) {
        return clampVolume(Math.round(progress / (float) OPLUS_VOLUME_SCALE));
    }

    private static int clampVolume(int volume) {
        return Math.max(0, Math.min(100, volume));
    }

    private static void bindOplusPhoneReturnRow(Object holder)
            throws ReflectiveOperationException {
        Context context = (Context) findFieldValue(holder, "context");
        View container = (View) findFieldValue(holder, "mainContent");
        TextView subtitle = (TextView) findFieldValue(holder, "subTitleText");
        subtitle.setText("点按停止 MiPlay 并返回本机");
        subtitle.setVisibility(View.VISIBLE);
        hideHolderView(holder, "seekBar");
        container.setOnClickListener(view -> stopMiPlay(context));
    }

    private static void captureOplusPhoneRowStyle(Object holder)
            throws ReflectiveOperationException {
        View container = (View) findFieldValue(holder, "mainContent");
        View iconArea = (View) findFieldValue(holder, "iconAreaLayout");
        TextView title = (TextView) findFieldValue(holder, "titleText");
        capturePhoneRowStyle(container, iconArea, title);
    }

    private static void capturePhoneRowStyle(View container, View iconArea, TextView title) {
        Drawable itemBackground = container.getBackground();
        Drawable iconBackground = iconArea.getBackground();
        if (itemBackground != null && itemBackground.getConstantState() != null) {
            phoneItemBackgroundState = itemBackground.getConstantState();
        }
        if (iconBackground != null && iconBackground.getConstantState() != null) {
            phoneIconBackgroundState = iconBackground.getConstantState();
        }
        phoneTitleColors = title.getTextColors();
    }

    private static void clearViewBackground(View view) {
        view.setBackground(null);
        view.setBackgroundTintList(null);
    }

    private static void applyPhoneRowStyle(
            Object holder,
            Context context,
            View container,
            View iconArea,
            int fallbackColor,
            boolean selected,
            boolean showVolumeStyle) {
        if (!showVolumeStyle) {
            if (!applyOplusStaticBackground(holder, container, selected)) {
                applyOplusItemBackground(context, container, fallbackColor);
            }
            clearViewBackground(iconArea);
            return;
        }

        Drawable itemBackground = newDrawable(context, phoneItemBackgroundState);
        if (itemBackground != null) {
            container.setBackground(itemBackground);
        } else {
            applyOplusItemBackground(context, container, fallbackColor);
        }

        Drawable iconBackground = newDrawable(context, phoneIconBackgroundState);
        if (iconBackground != null) {
            iconArea.setBackground(iconBackground);
            return;
        }
        GradientDrawable fallbackIconBackground = new GradientDrawable();
        fallbackIconBackground.setColor(Color.argb(230, 255, 255, 255));
        float radius = dp(context, 24);
        fallbackIconBackground.setCornerRadii(new float[]{
                radius, radius, 0, 0, 0, 0, radius, radius
        });
        iconArea.setBackground(fallbackIconBackground);
    }

    private static boolean applyOplusStaticBackground(
            Object holder,
            View container,
            boolean selected) {
        try {
            Method method = holder.getClass().getDeclaredMethod("getBackgroundRes", boolean.class);
            method.setAccessible(true);
            Object value = method.invoke(holder, selected);
            if (!(value instanceof Integer) || (Integer) value == 0) return false;
            container.setBackgroundTintList(null);
            container.setBackgroundResource((Integer) value);
            return true;
        } catch (Throwable error) {
            log("apply ColorOS static row background failed: " + error);
            return false;
        }
    }

    private static Drawable newDrawable(Context context, Drawable.ConstantState state) {
        return state == null ? null : state.newDrawable(context.getResources()).mutate();
    }

    private static void setViewHeight(Context context, View view, int heightDp) {
        ViewGroup.LayoutParams params = view.getLayoutParams();
        if (params == null) return;
        params.height = dp(context, heightDp);
        view.setLayoutParams(params);
    }

    private static void applyOplusItemBackground(Context context, View view, int color) {
        GradientDrawable background = new GradientDrawable();
        background.setColor(color);
        background.setCornerRadius(dp(context, 24));
        view.setBackground(background);
    }

    private static int controllerColor(Object adapter, String methodName, int fallback) {
        try {
            Object controller = findFieldValue(adapter, "mController");
            Object colorScheme = invokeNoArgs(controller, "getColorSchemeLegacy");
            Object value = invokeNoArgs(colorScheme, methodName);
            return value instanceof Integer ? (Integer) value : fallback;
        } catch (Throwable ignored) {
            return fallback;
        }
    }

    private static void refreshSpeakers(Context context) {
        refreshSpeakers(context, 0);
    }

    private static void refreshSpeakers(Context context, int attempt) {
        if (visibleOutputPage.get() == null) return;
        if (!SPEAKER_REFRESH_IN_FLIGHT.compareAndSet(false, true)) return;
        DISCOVERY_EXECUTOR.execute(() -> {
            List<Speaker> loaded = new ArrayList<>();
            try (Cursor cursor = context.getContentResolver().query(
                    SPEAKERS_URI,
                    null,
                    null,
                    null,
                    null)) {
                if (cursor != null) {
                    int id = cursor.getColumnIndexOrThrow("id");
                    int name = cursor.getColumnIndexOrThrow("name");
                    int host = cursor.getColumnIndexOrThrow("host");
                    int selected = cursor.getColumnIndexOrThrow("selected");
                    int state = cursor.getColumnIndexOrThrow("state");
                    int volume = cursor.getColumnIndexOrThrow("volume");
                    while (cursor.moveToNext()) {
                        loaded.add(new Speaker(
                                cursor.getString(id),
                                cursor.getString(name),
                                cursor.getString(host),
                                cursor.getInt(selected) == 1,
                                cursor.getString(state),
                                cursor.getInt(volume)));
                    }
                }
                if (!loaded.isEmpty()) {
                    speakers = Collections.unmodifiableList(new ArrayList<>(loaded));
                    for (String selectedId : new ArrayList<>(SELECTED_ORDER)) {
                        Speaker current = findSpeaker(selectedId);
                        if (current == null || !current.selected) SELECTED_ORDER.remove(selectedId);
                    }
                    for (Speaker discovered : loaded) {
                        if (discovered.selected && !SELECTED_ORDER.contains(discovered.id)) {
                            SELECTED_ORDER.add(discovered.id);
                        }
                    }
                }
                applyProviderStatus(context);
                log("discovered " + loaded.size() + " MiPlay speakers");
            } catch (Throwable error) {
                log("speaker query failed: " + error);
                applyProviderStatus(context);
            } finally {
                SPEAKER_REFRESH_IN_FLIGHT.set(false);
            }
            MAIN_HANDLER.post(SystemUiHookEntry::notifyAdapters);
            if (systemUiEnabled && loaded.isEmpty() && attempt < DISCOVERY_RETRY_LIMIT) {
                int nextAttempt = attempt + 1;
                log("MiPlay discovery empty, retry " + nextAttempt + "/"
                        + DISCOVERY_RETRY_LIMIT);
                MAIN_HANDLER.postDelayed(
                        () -> refreshSpeakers(context, nextAttempt),
                        DISCOVERY_RETRY_DELAY_MILLISECONDS);
            }
            updatePreparingRefresh(context);
        });
    }

    private static boolean applyProviderStatus(Context context) {
        try {
            Bundle status = context.getContentResolver().call(
                    SPEAKERS_URI,
                    METHOD_STATUS,
                    null,
                    null);
            if (status == null) return false;
            boolean enabled = status.getBoolean(KEY_ENABLED, true);
            String state = status.getString(KEY_STATE, "idle");
            ArrayList<String> activeIds = status.getStringArrayList(KEY_RENDERER_IDS);
            if (activeIds == null) activeIds = new ArrayList<>();
            ArrayList<String> activeHosts = status.getStringArrayList(KEY_RENDERER_HOSTS);
            if (activeHosts == null) activeHosts = new ArrayList<>();
            List<Speaker> updated = new ArrayList<>();
            List<String> selectedOrder = new ArrayList<>();
            boolean changed = enabled != systemUiEnabled;
            systemUiEnabled = enabled;
            for (Speaker speaker : speakers) {
                boolean selected = activeIds.contains(speaker.id)
                        || (!speaker.host.isEmpty() && activeHosts.contains(speaker.host));
                String speakerState = selected ? state : "idle";
                changed |= selected != speaker.selected || !speakerState.equals(speaker.state);
                updated.add(speaker.withState(
                        selected,
                        speakerState,
                        speaker.volume));
            }
            // Keep volume receiver indices aligned with the running session, not discovery order.
            for (int index = 0; index < Math.max(activeIds.size(), activeHosts.size()); index++) {
                String activeId = index < activeIds.size() ? activeIds.get(index) : "";
                String activeHost = index < activeHosts.size() ? activeHosts.get(index) : "";
                for (Speaker speaker : updated) {
                    if (speaker.selected && (speaker.id.equals(activeId)
                            || (!activeHost.isEmpty() && speaker.host.equals(activeHost)))) {
                        if (!selectedOrder.contains(speaker.id)) selectedOrder.add(speaker.id);
                        break;
                    }
                }
            }
            speakers = Collections.unmodifiableList(updated);
            if (!SELECTED_ORDER.equals(selectedOrder)) {
                changed = true;
                SELECTED_ORDER.clear();
                SELECTED_ORDER.addAll(selectedOrder);
            }
            if (changed) log("applied live cast status " + state + " to " + selectedOrder.size()
                    + " MiPlay speaker(s)");
            return changed;
        } catch (Throwable error) {
            log("provider status query failed: " + error);
            return false;
        }
    }

    private static void updatePreparingRefresh(Context context) {
        boolean preparing = false;
        for (Speaker speaker : speakerSnapshot()) {
            if ("preparing".equals(speaker.state)) {
                preparing = true;
                break;
            }
        }
        if (!preparing) {
            preparingDeadlineElapsedRealtime = 0L;
            return;
        }

        long now = SystemClock.elapsedRealtime();
        if (preparingDeadlineElapsedRealtime == 0L) {
            preparingDeadlineElapsedRealtime = now + PREPARING_TIMEOUT_MILLISECONDS;
        }
        if (now >= preparingDeadlineElapsedRealtime) {
            preparingDeadlineElapsedRealtime = 0L;
            MAIN_HANDLER.post(() -> {
                for (Speaker speaker : speakerSnapshot()) {
                    if ("preparing".equals(speaker.state)) {
                        log("MiPlay initialization timed out; clearing spinner");
                        stopMiPlay(context);
                        break;
                    }
                }
            });
            return;
        }
        if (!PREPARING_REFRESH_SCHEDULED.compareAndSet(false, true)) return;
        MAIN_HANDLER.postDelayed(() -> {
            PREPARING_REFRESH_SCHEDULED.set(false);
            refreshSpeakerStatus(context);
        }, PREPARING_REFRESH_DELAY_MILLISECONDS);
    }

    private static void launchSelection(Context context, Speaker speaker) {
        try {
            Intent intent = new Intent(ACTION_SELECTION)
                    .setComponent(new ComponentName(APP_PACKAGE, APP_ACTIVITY))
                    .putExtra(EXTRA_RENDERER_ID, speaker.id)
                    .putExtra(EXTRA_RENDERER_NAME, speaker.name)
                    .putExtra(EXTRA_RENDERER_HOST, speaker.host)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK
                            | Intent.FLAG_ACTIVITY_SINGLE_TOP
                            | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            context.startActivity(intent);
            log("selection requested for " + speaker.name);
        } catch (Throwable error) {
            log("selection launch failed: " + error);
        }
    }

    private static void toggleSpeaker(Context context, Speaker speaker) {
        if ("preparing".equals(speaker.state)) return;
        if (speaker.selected) {
            if (selectedSpeakerCount() <= 1) {
                stopMiPlay(context);
            } else {
                SELECTED_ORDER.remove(speaker.id);
                updateSpeaker(speaker.id, false, "idle", speaker.volume);
                sendRendererControl(context, ACTION_REMOVE_RENDERER, speaker);
                notifyAdapters();
            }
            return;
        }
        if (!systemUiEnabled || selectedSpeakerCount() >= MAX_MIPLAY_RECEIVERS) return;

        boolean firstSelection = selectedSpeakerCount() == 0;
        if (firstSelection) SELECTED_ORDER.clear();
        if (!SELECTED_ORDER.contains(speaker.id)) SELECTED_ORDER.add(speaker.id);
        preparingDeadlineElapsedRealtime =
                SystemClock.elapsedRealtime() + PREPARING_TIMEOUT_MILLISECONDS;
        updateSpeaker(speaker.id, false, "preparing", normalizedVolume(speaker));
        notifyAdapters();
        if (firstSelection) {
            sendRendererControl(context, ACTION_ROOT_START, speaker);
        } else {
            sendRendererControl(context, ACTION_ADD_RENDERER, speaker);
        }
        MAIN_HANDLER.postDelayed(() -> {
            refreshSpeakers(context);
        }, 1_800L);
    }

    private static void stopMiPlay(Context context) {
        try {
            sendControlBroadcast(context, new Intent(ACTION_STOP));
            preparingDeadlineElapsedRealtime = 0L;
            SELECTED_ORDER.clear();
            List<Speaker> updated = new ArrayList<>();
            for (Speaker speaker : speakerSnapshot()) {
                updated.add(speaker.withSelected(false, "idle"));
            }
            speakers = Collections.unmodifiableList(updated);
            notifyAdapters();
        } catch (Throwable error) {
            log("stop request failed: " + error);
        }
    }

    private static void sendRendererControl(Context context, String action, Speaker speaker) {
        Intent intent = new Intent(action)
                .putExtra(EXTRA_RENDERER_ID, speaker.id)
                .putExtra(EXTRA_RENDERER_NAME, speaker.name)
                .putExtra(EXTRA_RENDERER_HOST, speaker.host);
        sendControlBroadcast(context, intent);
    }

    private static void sendVolume(Context context, int volume, Integer receiverIndex) {
        Intent intent = new Intent(ACTION_SET_VOLUME)
                .putExtra(EXTRA_VOLUME, Math.max(0, Math.min(100, volume)));
        if (receiverIndex != null && receiverIndex >= 0) {
            intent.putExtra(EXTRA_RECEIVER_INDEX, receiverIndex);
        }
        sendControlBroadcast(context, intent);
    }

    private static void sendControlBroadcast(Context context, Intent intent) {
        try {
            intent.setComponent(new ComponentName(APP_PACKAGE, CONTROL_RECEIVER));
            context.sendBroadcast(intent);
        } catch (Throwable error) {
            log("SystemUI control broadcast failed: " + error);
        }
    }

    private static boolean isPhoneMediaItem(Object adapter, int position) {
        try {
            List<?> mediaItems = mediaItemList(adapter);
            if (position < 0 || position >= mediaItems.size()) return false;
            Object optionalValue = invokeNoArgs(mediaItems.get(position), "getMediaDevice");
            if (!(optionalValue instanceof Optional)) return false;
            Object device = ((Optional<?>) optionalValue).orElse(null);
            if (device == null) return false;
            String className = device.getClass().getName().toLowerCase(Locale.ROOT);
            return className.contains("phonemediadevice")
                    || className.contains("builtin")
                    || className.contains("wiredmediadevice");
        } catch (Throwable ignored) {
            return false;
        }
    }

    private static void rememberAdapter(Object adapter) {
        if (adapter == null) return;
        for (WeakReference<Object> reference : ADAPTERS) {
            if (reference.get() == adapter) return;
        }
        ADAPTERS.add(new WeakReference<>(adapter));
    }

    private static void notifyAdapters() {
        View recycler = null;
        Object page = visibleOutputPage.get();
        if (page != null) {
            try {
                Object value = findFieldValue(page, "devicesRecyclerView");
                if (value instanceof View) {
                    recycler = (View) value;
                    rememberAdapter(invokeNoArgs(value, "getAdapter"));
                }
            } catch (Throwable error) {
                log("output recycler lookup failed: " + error);
            }
        }
        Iterator<WeakReference<Object>> iterator = ADAPTERS.iterator();
        while (iterator.hasNext()) {
            WeakReference<Object> reference = iterator.next();
            Object adapter = reference.get();
            if (adapter == null) {
                ADAPTERS.remove(reference);
                continue;
            }
            try {
                Method notify = adapter.getClass().getMethod("notifyDataSetChanged");
                notify.invoke(adapter);
            } catch (Throwable error) {
                log("adapter refresh failed: " + error);
            }
        }
        if (recycler != null) recycler.requestLayout();
        log("refreshed " + ADAPTERS.size() + " output adapter(s), "
                + speakers.size() + " MiPlay speaker(s)");
    }

    private static boolean hasSelectedSpeaker() {
        for (Speaker speaker : speakerSnapshot()) {
            if (speaker.selected) return true;
        }
        return false;
    }

    private static int selectedSpeakerCount() {
        int count = 0;
        for (Speaker speaker : speakerSnapshot()) {
            if (speaker.selected) count++;
        }
        return count;
    }

    private static int selectedSpeakerIndex(String id) {
        return SELECTED_ORDER.indexOf(id);
    }

    private static int normalizedVolume(Speaker speaker) {
        return speaker.volume >= 0 ? Math.max(0, Math.min(100, speaker.volume)) : 50;
    }

    private static int selectedAverageVolume() {
        int total = 0;
        int count = 0;
        for (Speaker speaker : speakerSnapshot()) {
            if (!speaker.selected) continue;
            total += normalizedVolume(speaker);
            count++;
        }
        return count == 0 ? 50 : total / count;
    }

    private static Speaker findSpeaker(String id) {
        for (Speaker speaker : speakerSnapshot()) {
            if (speaker.id.equals(id)) return speaker;
        }
        return null;
    }

    private static void updateSpeaker(String id, boolean selected, String state, int volume) {
        List<Speaker> updated = new ArrayList<>();
        for (Speaker speaker : speakerSnapshot()) {
            updated.add(speaker.id.equals(id)
                    ? speaker.withState(selected, state, volume)
                    : speaker);
        }
        speakers = Collections.unmodifiableList(updated);
    }

    private static void updateSpeakerVolume(String id, int volume) {
        Speaker speaker = findSpeaker(id);
        if (speaker == null) return;
        updateSpeaker(id, speaker.selected, speaker.state, volume);
    }

    private static void updateSelectedVolumes(int volume) {
        List<Speaker> updated = new ArrayList<>();
        for (Speaker speaker : speakerSnapshot()) {
            updated.add(speaker.selected
                    ? speaker.withState(true, speaker.state, volume)
                    : speaker);
        }
        speakers = Collections.unmodifiableList(updated);
    }

    private static String speakerSubtitle(Speaker speaker) {
        if ("preparing".equals(speaker.state)) return "正在连接 · " + speaker.host;
        if ("streaming".equals(speaker.state)) return "正在投送 · " + speaker.host;
        if ("paused".equals(speaker.state)) return "已暂停 · " + speaker.host;
        if (speaker.selected) return "MiPlay 已选择 · " + speaker.host;
        return "MiPlay · " + speaker.host;
    }

    private static int baseItemCount(Object adapter) throws ReflectiveOperationException {
        return mediaItemList(adapter).size();
    }

    private static List<?> mediaItemList(Object adapter) throws ReflectiveOperationException {
        Object value = findFieldValue(adapter, "mMediaItemList");
        return value instanceof List ? (List<?>) value : Collections.emptyList();
    }

    private static boolean isOplusAdapter(Object adapter) {
        return adapter != null
                && "com.oplus.systemui.media.output.OplusMediaOutputAdapter"
                .equals(adapter.getClass().getName());
    }

    private static List<Speaker> speakerSnapshot() {
        List<Speaker> current = speakers;
        if (systemUiEnabled) return current;
        List<Speaker> active = new ArrayList<>();
        for (Speaker speaker : current) {
            if (speaker.selected || "preparing".equals(speaker.state)) active.add(speaker);
        }
        return active;
    }

    private static void hideHolderView(Object holder, String fieldName)
            throws ReflectiveOperationException {
        Object value = findFieldValue(holder, fieldName);
        if (value instanceof View) ((View) value).setVisibility(View.GONE);
    }

    private static Object findFieldValue(Object target, String fieldName)
            throws ReflectiveOperationException {
        Field field = findField(target.getClass(), fieldName);
        field.setAccessible(true);
        return field.get(target);
    }

    private static void setFieldValue(Object target, String fieldName, Object value)
            throws ReflectiveOperationException {
        Field field = findField(target.getClass(), fieldName);
        field.setAccessible(true);
        field.set(target, value);
    }

    private static Field findField(Class<?> type, String name) throws NoSuchFieldException {
        Class<?> cursor = type;
        while (cursor != null) {
            try {
                return cursor.getDeclaredField(name);
            } catch (NoSuchFieldException ignored) {
                cursor = cursor.getSuperclass();
            }
        }
        throw new NoSuchFieldException(type.getName() + "." + name);
    }

    private static Object invokeNoArgs(Object target, String methodName)
            throws ReflectiveOperationException {
        Method method = findMethod(target.getClass(), methodName);
        method.setAccessible(true);
        return method.invoke(target);
    }

    private static Object invokeOneIntArg(Object target, String methodName, int value)
            throws ReflectiveOperationException {
        Method method = findOneArgMethod(target.getClass(), methodName);
        method.setAccessible(true);
        return method.invoke(target, value);
    }

    private static Method findMethod(Class<?> type, String name) throws NoSuchMethodException {
        Class<?> cursor = type;
        while (cursor != null) {
            for (Method method : cursor.getDeclaredMethods()) {
                if (method.getName().equals(name) && method.getParameterCount() == 0) return method;
            }
            cursor = cursor.getSuperclass();
        }
        throw new NoSuchMethodException(type.getName() + "." + name);
    }

    private static Method findOneArgMethod(Class<?> type, String name)
            throws NoSuchMethodException {
        Class<?> cursor = type;
        while (cursor != null) {
            for (Method method : cursor.getDeclaredMethods()) {
                if (method.getName().equals(name) && method.getParameterCount() == 1) {
                    return method;
                }
            }
            cursor = cursor.getSuperclass();
        }
        throw new NoSuchMethodException(type.getName() + "." + name);
    }

    private static int dp(Context context, int value) {
        return Math.round(value * context.getResources().getDisplayMetrics().density);
    }

    private static void log(String message) {
        SystemUiHookEntry current = instance;
        if (current != null) {
            current.log(android.util.Log.INFO, TAG, message);
        } else {
            android.util.Log.i(TAG, message);
        }
    }

    private interface VolumeListener {
        void onChanged(int volume);

        void onStopped(int volume);
    }

    private static final class Speaker {
        final String id;
        final String name;
        final String host;
        final boolean selected;
        final String state;
        final int volume;

        Speaker(String id, String name, String host, boolean selected, String state, int volume) {
            this.id = id;
            this.name = name;
            this.host = host;
            this.selected = selected;
            this.state = state;
            this.volume = volume;
        }

        Speaker withSelected(boolean newSelected, String newState) {
            return new Speaker(id, name, host, newSelected, newState, volume);
        }

        Speaker withState(boolean newSelected, String newState, int newVolume) {
            return new Speaker(id, name, host, newSelected, newState, newVolume);
        }
    }
}
