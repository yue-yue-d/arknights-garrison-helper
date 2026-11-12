package com.weishu.helper

import android.annotation.SuppressLint
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.content.res.Configuration
import android.graphics.PixelFormat
import android.os.Build
import android.util.DisplayMetrics
import android.view.Gravity
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.WindowManager
import android.view.WindowManager.LayoutParams as LP
import android.widget.FrameLayout
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleService
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.savedstate.SavedStateRegistry
import androidx.savedstate.SavedStateRegistryController
import androidx.savedstate.SavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * 游戏上方的悬浮窗：悬浮球 ⇄ 面板。
 * 只是一个普通的系统悬浮窗（显示在其他应用上层），不读写游戏内存、不注入。
 */
class OverlayService : LifecycleService(), SavedStateRegistryOwner {
    companion object {
        private const val ACTION_STOP = "com.weishu.helper.STOP"
        var running by mutableStateOf(false)
            private set

        fun start(ctx: Context) = ContextCompat.startForegroundService(ctx, Intent(ctx, OverlayService::class.java))
        fun stop(ctx: Context) { ctx.stopService(Intent(ctx, OverlayService::class.java)) }
    }

    private val ssrc = SavedStateRegistryController.create(this)
    override val savedStateRegistry: SavedStateRegistry get() = ssrc.savedStateRegistry

    private lateinit var wm: WindowManager
    private var ballView: View? = null
    private var panelView: View? = null
    private var pillView: View? = null
    private val ballLp = baseLp(0, 0, LP.FLAG_NOT_FOCUSABLE or LP.FLAG_LAYOUT_NO_LIMITS or LP.FLAG_LAYOUT_IN_SCREEN)
    private val panelLp = baseLp(0, 0, 0)
    private val pillLp = baseLp(LP.WRAP_CONTENT, LP.WRAP_CONTENT, LP.FLAG_NOT_FOCUSABLE or LP.FLAG_LAYOUT_IN_SCREEN)
    private var expanded = false
    private var through = false
    private var focusable = false
    private var ballHidden = false
    /** 悬浮球贴着哪边：L / R / 空。转屏后按这个重新贴边 */
    private var ballSide = ""

    private val host = object : PanelHost {
        override val overlay = true
        override fun setFocusable(on: Boolean) = this@OverlayService.setFocusable(on)
        override fun collapse() = this@OverlayService.collapse()
        override fun close() { stopSelf() }
        override fun toggleClickThrough() = this@OverlayService.setThrough(!through)
        override fun movePanel(dx: Float, dy: Float) = this@OverlayService.movePanel(dx, dy)
    }

    override fun onCreate() {
        ssrc.performAttach()
        ssrc.performRestore(null)
        super.onCreate()
        wm = getSystemService(WINDOW_SERVICE) as WindowManager
        startForegroundNotice()
        running = true
        showBall()
        // 设置页改了大小 / 宽度 / 不透明度：悬浮窗跟着变
        lifecycleScope.launch {
            snapshotFlow { val s = Store.settings.value; listOf(s.ballSize, s.width, s.opacity, if (s.edgeHide) 1f else 0f) }.collect {
                if (ballView != null) { layoutBall(); snapBall(false) }
                if (panelView != null) { applyPanelLp(); safeUpdate(panelView, panelLp) }
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        super.onStartCommand(intent, flags, startId)
        if (intent?.action == ACTION_STOP) stopSelf()
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        listOf(ballView, panelView, pillView).forEach { v -> v?.let { runCatching { wm.removeView(it) } } }
        ballView = null; panelView = null; pillView = null
        running = false
        super.onDestroy()
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        // 横竖屏切换：重新排一下
        if (panelView != null) { applyPanelLp(); safeUpdate(panelView, panelLp) }
        if (ballView != null) {
            // 横竖屏切换后屏幕宽度变了：原来贴哪边就贴回哪边
            when (ballSide) { "R" -> ballLp.x = screen().first; "L" -> ballLp.x = -ballLp.width }
            snapBall(true)
        }
    }

    // ---------- 工具 ----------
    private fun dp(v: Float) = (v * resources.displayMetrics.density).roundToInt()

    private fun screen(): Pair<Int, Int> =
        if (Build.VERSION.SDK_INT >= 30) wm.currentWindowMetrics.bounds.let { it.width() to it.height() }
        else DisplayMetrics().also { @Suppress("DEPRECATION") wm.defaultDisplay.getRealMetrics(it) }.let { it.widthPixels to it.heightPixels }

    @SuppressLint("InternalInsetResource", "DiscouragedApi")
    private fun statusBarHeight(): Int {
        val id = resources.getIdentifier("status_bar_height", "dimen", "android")
        return if (id > 0) resources.getDimensionPixelSize(id) else dp(24f)
    }

    private fun baseLp(w: Int, h: Int, flags: Int) = LP(w, h, LP.TYPE_APPLICATION_OVERLAY, flags, PixelFormat.TRANSLUCENT).apply {
        gravity = Gravity.TOP or Gravity.START
        if (Build.VERSION.SDK_INT >= 28) layoutInDisplayCutoutMode = LP.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
    }

    private fun safeUpdate(v: View?, lp: LP) { if (v != null) runCatching { wm.updateViewLayout(v, lp) } }

    /** Compose 从窗口的根 View 往下找生命周期，所以根 View 和 ComposeView 都要设。 */
    private fun <V : View> V.owned(): V = apply {
        setViewTreeLifecycleOwner(this@OverlayService)
        setViewTreeSavedStateRegistryOwner(this@OverlayService)
    }

    private fun compose(content: @Composable () -> Unit) = ComposeView(this).owned().apply { setContent(content) }

    // ---------- 悬浮球 ----------
    private fun layoutBall() {
        val size = dp(Store.settings.value.ballSize + 8f)
        ballLp.width = size
        ballLp.height = size
    }

    @SuppressLint("ClickableViewAccessibility")
    private fun showBall() {
        val s = Store.settings.value
        layoutBall()
        val (_, sh) = screen()
        ballLp.x = if (s.ballX != -1) s.ballX else 0
        ballLp.y = if (s.ballY >= 0) s.ballY else sh / 3
        val frame = FrameLayout(this).owned()
        frame.addView(compose { BallFace() })
        // 拖动用原始坐标处理，比在 Compose 里跟着窗口一起移动更稳
        val slop = ViewConfiguration.get(this).scaledTouchSlop
        var downX = 0f; var downY = 0f; var startX = 0; var startY = 0; var dragging = false
        frame.setOnTouchListener { _, e ->
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = e.rawX; downY = e.rawY; startX = ballLp.x; startY = ballLp.y; dragging = false
                    if (ballHidden) { ballLp.alpha = 1f; safeUpdate(ballView, ballLp) }
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.rawX - downX; val dy = e.rawY - downY
                    if (!dragging && (abs(dx) > slop || abs(dy) > slop)) dragging = true
                    if (dragging) { ballLp.x = startX + dx.roundToInt(); ballLp.y = startY + dy.roundToInt(); safeUpdate(ballView, ballLp) }
                }
                MotionEvent.ACTION_UP -> if (dragging) snapBall(true) else expand()
                MotionEvent.ACTION_CANCEL -> snapBall(false)
            }
            true
        }
        ballView = frame
        wm.addView(frame, ballLp)
        snapBall(false)
    }

    /** 拖到屏幕边缘附近：贴边；开了「贴边隐藏」就藏一半、变半透明。 */
    private fun snapBall(save: Boolean) {
        val s = Store.settings.value
        val (sw, sh) = screen()
        val size = ballLp.width
        ballLp.y = ballLp.y.coerceIn(0, (sh - size).coerceAtLeast(0))
        val left = ballLp.x + size / 2 < sw / 2
        val near = if (left) ballLp.x < dp(40f) else ballLp.x + size > sw - dp(40f)
        ballHidden = false
        ballSide = if (near) (if (left) "L" else "R") else ""
        if (near) {
            ballHidden = s.edgeHide
            ballLp.x = if (left) (if (s.edgeHide) -size / 2 else 0) else (if (s.edgeHide) sw - size / 2 else sw - size)
        } else ballLp.x = ballLp.x.coerceIn(0, (sw - size).coerceAtLeast(0))
        ballLp.alpha = if (ballHidden) 0.5f else 1f
        safeUpdate(ballView, ballLp)
        if (save) Store.update { it.copy(ballX = ballLp.x, ballY = ballLp.y) }
    }

    @Composable
    private fun BallFace() {
        val s by Store.settings
        val ui = rememberUiTheme(s)
        Box(Modifier.fillMaxSize().padding(4.dp)) {
            AssetImage(
                s.ballImage,
                Modifier.fillMaxSize().shadow(4.dp, CircleShape).clip(CircleShape).background(ui.panel).border(3.dp, ui.ball, CircleShape),
            )
        }
    }

    // ---------- 面板 ----------
    private fun applyPanelLp() {
        val s = Store.settings.value
        val (sw, sh) = screen()
        val m = dp(6f)
        // 状态栏在悬浮窗上层，会吃掉顶部的点击：面板从状态栏下面开始
        val top = statusBarHeight() + m
        panelLp.width = dp(s.width).coerceAtMost(sw - 2 * m)
        panelLp.height = sh - top - m
        panelLp.x = panelLp.x.coerceIn(0, (sw - panelLp.width).coerceAtLeast(0))
        panelLp.y = panelLp.y.coerceIn(top, (sh - panelLp.height).coerceAtLeast(top))
        var f = LP.FLAG_NOT_TOUCH_MODAL or LP.FLAG_WATCH_OUTSIDE_TOUCH or LP.FLAG_LAYOUT_IN_SCREEN
        if (!focusable || through) f = f or LP.FLAG_NOT_FOCUSABLE
        if (through) f = f or LP.FLAG_NOT_TOUCHABLE
        panelLp.flags = f
        // Android 12+ 规定：要让触摸穿过悬浮窗，窗口不透明度不能超过 0.8
        panelLp.alpha = if (through) min(s.opacity, 0.7f) else s.opacity
        // 打字时面板跟着键盘缩短，输入框不会被挡住
        panelLp.softInputMode = WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE
    }

    private fun expand() {
        if (expanded) return
        expanded = true
        ballView?.let { runCatching { wm.removeView(it) } }
        ballView = null
        val s = Store.settings.value
        panelLp.x = s.panelX
        panelLp.y = s.panelY
        applyPanelLp()
        val frame = object : FrameLayout(this) {
            override fun dispatchTouchEvent(ev: MotionEvent): Boolean {
                if (ev.actionMasked == MotionEvent.ACTION_OUTSIDE) { if (this@OverlayService.focusable) FocusSignal.clear++; return false }
                return super.dispatchTouchEvent(ev)
            }

            override fun dispatchKeyEvent(event: KeyEvent): Boolean {
                if (event.keyCode == KeyEvent.KEYCODE_BACK) {
                    if (event.action == KeyEvent.ACTION_UP) { if (this@OverlayService.focusable) FocusSignal.clear++ else collapse() }
                    return true
                }
                return super.dispatchKeyEvent(event)
            }
        }
        frame.owned()
        frame.addView(compose { PanelRoot(host) })
        panelView = frame
        wm.addView(frame, panelLp)
    }

    private fun collapse() {
        if (!expanded) return
        setThrough(false)
        expanded = false
        focusable = false
        panelView?.let { runCatching { wm.removeView(it) } }
        panelView = null
        showBall()
    }

    private fun movePanel(dx: Float, dy: Float) {
        panelLp.x += dx.roundToInt()
        panelLp.y += dy.roundToInt()
        applyPanelLp()
        safeUpdate(panelView, panelLp)
        Store.update { it.copy(panelX = panelLp.x, panelY = panelLp.y) }
    }

    private fun setFocusable(on: Boolean) {
        if (focusable == on || panelView == null) return
        focusable = on
        applyPanelLp()
        safeUpdate(panelView, panelLp)
    }

    // ---------- 穿透 ----------
    private fun setThrough(on: Boolean) {
        if (through == on) return
        through = on
        if (on) { focusable = false; FocusSignal.clear++ }
        applyPanelLp()
        safeUpdate(panelView, panelLp)
        if (on) {
            pillLp.x = panelLp.x + dp(10f)
            pillLp.y = panelLp.y + dp(8f)
            val v = compose { ThroughPill() }
            v.setOnClickListener { setThrough(false) }
            pillView = v
            wm.addView(v, pillLp)
        } else {
            pillView?.let { runCatching { wm.removeView(it) } }
            pillView = null
        }
    }

    @Composable
    private fun ThroughPill() {
        val s by Store.settings
        val ui = rememberUiTheme(s)
        Box(
            Modifier.shadow(4.dp, RoundedCornerShape(16.dp)).clip(RoundedCornerShape(16.dp)).background(ui.accent)
                .combinedClick(onClick = { setThrough(false) }).padding(horizontal = 12.dp, vertical = 7.dp),
            contentAlignment = Alignment.Center,
        ) { Text("穿透中 · 点这里取消", color = Color(0xFF0E1215), fontSize = 13.sp, fontWeight = FontWeight.Bold) }
    }

    // ---------- 常驻通知 ----------
    private fun startForegroundNotice() {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel("overlay", "悬浮窗", NotificationManager.IMPORTANCE_MIN).apply { setShowBadge(false) })
        val flags = PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), flags)
        val stop = PendingIntent.getService(this, 1, Intent(this, OverlayService::class.java).setAction(ACTION_STOP), flags)
        val n = NotificationCompat.Builder(this, "overlay")
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle("卫戍协议助手 · 悬浮窗运行中")
            .setContentText("点这里打开主界面")
            .setContentIntent(open)
            .addAction(0, "关闭悬浮窗", stop)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_MIN)
            .build()
        ServiceCompat.startForeground(this, 1, n, if (Build.VERSION.SDK_INT >= 34) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0)
    }
}
