package com.weishu.helper

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings as SysSettings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { MainScreen(this) }
    }
}

@Composable
private fun MainScreen(act: ComponentActivity) {
    val s by Store.settings
    val ui = rememberUiTheme(s)
    var canOverlay by remember { mutableStateOf(SysSettings.canDrawOverlays(act)) }
    val lifecycle = LocalLifecycleOwner.current
    // 从系统设置授权回来后刷新状态
    LaunchedEffect(lifecycle) { lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { canOverlay = SysSettings.canDrawOverlays(act) } }
    val notif = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { startOverlay(act) }

    Column(Modifier.fillMaxSize().background(ui.panel).systemBarsPadding()) {
        CompositionLocalProvider(LocalUi provides ui) {
            Row(
                Modifier.fillMaxWidth().padding(8.dp).clip(RoundedCornerShape(10.dp)).background(ui.accent.copy(alpha = 0.14f)).padding(10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    T("游戏悬浮窗", 1f, ui.accent, true)
                    T(
                        when {
                            !canOverlay -> "需要先允许「显示在其他应用上层」，悬浮窗才能盖在明日方舟上面。"
                            OverlayService.running -> "悬浮窗运行中。切到明日方舟，点悬浮球展开布阵 / 资料。"
                            else -> "开启后会出现一个悬浮球，打卫戍协议时点它展开面板。"
                        },
                        0.8f, Pal.Sub,
                    )
                }
                when {
                    !canOverlay -> Chip("去授权", true) {
                        act.startActivity(Intent(SysSettings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:${act.packageName}")))
                    }
                    OverlayService.running -> Chip("关闭悬浮窗") { OverlayService.stop(act) }
                    else -> Chip("开启悬浮窗", true) {
                        if (Build.VERSION.SDK_INT >= 33) notif.launch(Manifest.permission.POST_NOTIFICATIONS) else startOverlay(act)
                    }
                }
            }
        }
        PanelRoot(object : PanelHost { override val overlay = false }, Modifier.weight(1f))
    }
}

private fun startOverlay(act: ComponentActivity) {
    OverlayService.start(act)
    Toast.makeText(act, "悬浮窗已开启，切到游戏后点悬浮球", Toast.LENGTH_SHORT).show()
    act.moveTaskToBack(true)
}
