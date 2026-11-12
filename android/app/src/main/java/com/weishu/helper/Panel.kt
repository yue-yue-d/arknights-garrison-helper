package com.weishu.helper

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt

/** 布阵 / 资料 / 设置 面板。悬浮窗和主界面都用它。 */
@Composable
fun PanelRoot(host: PanelHost, modifier: Modifier = Modifier) {
    val s by Store.settings
    val ui = rememberUiTheme(s)
    val sheet = remember { SheetState() }
    CompositionLocalProvider(LocalUi provides ui, LocalHost provides host, LocalSheet provides sheet) {
        val shape = RoundedCornerShape(if (host.overlay) 16.dp else 0.dp)
        Column(
            modifier
                .fillMaxSize()
                .clip(shape)
                .background(Brush.verticalGradient(listOf(ui.panel.mix(Color.White, 0.05f), ui.panel)))
                .then(if (host.overlay) Modifier.border(1.dp, ui.accent.copy(alpha = 0.45f), shape) else Modifier)
                .imePadding(),   // 键盘弹出时内容区让出位置，输入框滚到键盘上面
        ) {
            val compact = host.overlay && FocusSignal.typing
            if (!compact) Header(host)
            if (!compact) Row(Modifier.padding(horizontal = 8.dp, vertical = 4.dp)) {
                for (t in listOf("布阵", "资料")) Chip(t, !Shared.showSettings && Shared.tab == t, k = 1f) {
                    Shared.showSettings = false
                    Shared.tab = t
                    Store.update { it.copy(tab = t) }
                }
            }
            Box(Modifier.weight(1f)) {
                SheetHost(sheet) {
                    when {
                        Shared.showSettings -> SettingsScreen()
                        Shared.tab == "资料" -> RefScreen()
                        else -> PlannerScreen()
                    }
                }
            }
        }
    }
}

@Composable
private fun Header(host: PanelHost) {
    val ui = LocalUi.current
    val s by Store.settings
    Row(
        Modifier
            .fillMaxWidth()
            .background(Brush.horizontalGradient(listOf(ui.accent.copy(alpha = 0.22f), Color.Transparent)))
            .pointerInput(host.overlay) { if (host.overlay) detectDragGestures { c, d -> c.consume(); host.movePanel(d.x, d.y) } }
            .padding(horizontal = 8.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        AssetImage(s.ballImage, Modifier.size(26.dp).clip(CircleShape).border(1.5.dp, ui.ball, CircleShape))
        Spacer(Modifier.width(6.dp))
        Column(Modifier.weight(1f)) {
            T("卫戍协议·规程　${Shared.planner.map}", 1f, ui.accent, true, maxLines = 1)
            T(if (host.overlay) "悬浮窗 · 拖动标题栏可移动" else "离线资料 · 布阵规划", 0.72f, Pal.Sub, maxLines = 1)
        }
        if (host.overlay) Chip("穿透", k = 0.85f) { host.toggleClickThrough() }
        Chip("⚙", Shared.showSettings, k = 0.95f) { Shared.showSettings = !Shared.showSettings }
        if (host.overlay) {
            Chip("◀", k = 0.95f) { host.collapse() }
            Chip("✕", k = 0.95f) { host.close() }
        }
    }
}

// ======================= 设置 =======================
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen() {
    val s by Store.settings
    val ui = LocalUi.current
    val sheet = LocalSheet.current
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 10.dp)) {
        Section("外观")
        T("主题色　${s.accent}", 0.85f, Pal.Sub)
        Swatches(AccentPresets.map { it.second }, s.accent) { c -> Store.update { it.copy(accent = c) } }
        T("面板底色　${s.panel}", 0.85f, Pal.Sub)
        Swatches(PanelPresets.map { it.second }, s.panel, ring = true) { c -> Store.update { it.copy(panel = c) } }
        T("悬浮球光圈　" + s.ballColor.ifEmpty { "跟随主题色" }, 0.85f, Pal.Sub)
        Swatches(listOf("") + AccentPresets.map { it.second }, s.ballColor) { c -> Store.update { it.copy(ballColor = c) } }

        T("悬浮球图片", 0.85f, Pal.Sub, modifier = Modifier.padding(top = 4.dp))
        FlowRow {
            val opts = (1..8).map { "图片/通用/奖杯$it.png" } + "图片/通用/卫戍节点.png"
            for (rel in opts) BallChoice(rel, rel == s.ballImage) { Store.update { it.copy(ballImage = rel) } }
            Chip("用干员头像…", k = 0.85f) {
                sheet.show {
                    T("选一个干员头像当悬浮球", 1f, ui.accent, true)
                    FlowRow(Modifier.padding(top = 6.dp)) {
                        for (o in GameData.ops.sortedWith(compareBy({ -it.tier }, { it.name })))
                            BallChoice(o.img, o.img == s.ballImage) { Store.update { it.copy(ballImage = o.img) }; sheet.hide() }
                    }
                }
            }
        }
        SliderRow("悬浮球大小　${s.ballSize.roundToInt()}", s.ballSize, 36f..84f) { v -> Store.update { it.copy(ballSize = v) } }
        SwitchRow("悬浮球拖到屏幕边缘时半隐藏、变半透明", s.edgeHide) { v -> Store.update { it.copy(edgeHide = v) } }
        Chip("恢复默认外观", k = 0.88f) {
            Store.update { it.copy(accent = "#41DBB2", panel = "#141A1F", ballColor = "", ballImage = "图片/通用/奖杯8.png", ballSize = 52f, edgeHide = true) }
        }

        Section("显示")
        SliderRow("悬浮窗宽度　${s.width.roundToInt()}", s.width, 280f..560f) { v -> Store.update { it.copy(width = v) } }
        SliderRow("不透明度　${"%.2f".format(s.opacity)}", s.opacity, 0.5f..1f) { v -> Store.update { it.copy(opacity = v) } }
        SliderRow("字号　${"%.2f".format(s.fontScale)}", s.fontScale, 0.8f..1.4f) { v -> Store.update { it.copy(fontScale = v) } }
        SwitchRow("布阵部署顺序：先按列，同一列再从上到下", s.colFirst) { v -> Store.update { it.copy(colFirst = v) } }
        T("游戏内说明是「从左至右、从上到下」依次部署。实际顺序以游戏为准，不对就关掉改成按行。", 0.76f, Pal.Sub)

        Section("悬浮窗用法")
        T(
            "· 悬浮球可以拖动，拖到屏幕边缘会半隐藏；点一下展开面板。\n" +
                "· 面板标题栏可以拖动；「◀」收起成悬浮球，「✕」关闭悬浮窗。\n" +
                "· 「穿透」：面板变半透明，手指直接点到游戏上；点左上角的「取消穿透」恢复。\n" +
                "· 搜索框只有点它时才会弹出键盘，平时不会抢游戏的焦点。",
            0.84f,
        )

        Section("数据")
        T("${GameData.season}　更新于 ${GameData.updated}（来源 PRTS）", 0.84f)
        T("干员 ${GameData.ops.size}　支援 ${GameData.support.size}　盟约 ${GameData.bonds.size}　策略 ${GameData.strategies.size}　装备 ${GameData.items.size}　机变 ${GameData.phases.size}", 0.8f, Pal.Sub)
        T("数据和图片整理自 PRTS 维基（prts.wiki），游戏素材版权归鹰角网络所有，仅供玩家交流使用。", 0.78f, Pal.Sub)

        Section("关于")
        T("卫戍协议助手 安卓版 v1.1.0 · 外置悬浮窗：不读写游戏内存、不注入、不改游戏文件，地图需要自己选。", 0.82f)
        Spacer(Modifier.size(24.dp))
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Swatches(colors: List<String>, current: String, ring: Boolean = false, onPick: (String) -> Unit) {
    val ui = LocalUi.current
    FlowRow(Modifier.padding(bottom = 4.dp)) {
        for (c in colors) {
            val sel = c.equals(current, true)
            Box(
                Modifier.padding(end = 6.dp, bottom = 4.dp).size(28.dp).clip(CircleShape)
                    .background(if (c.isEmpty()) ui.accent.copy(alpha = 0.35f) else hex(c))
                    .border(if (sel) 3.dp else 1.dp, if (sel) Color.White else if (ring) Color(0x66FFFFFF) else Color(0x33FFFFFF), CircleShape)
                    .combinedClick(onClick = { onPick(c) }),
                contentAlignment = Alignment.Center,
            ) { if (c.isEmpty()) T("随", 0.75f, bold = true) }
        }
    }
}

@Composable
private fun BallChoice(rel: String, selected: Boolean, onClick: () -> Unit) {
    val ui = LocalUi.current
    AssetImage(
        rel,
        Modifier.padding(end = 6.dp, bottom = 6.dp).size(40.dp).clip(CircleShape)
            .border(if (selected) 3.dp else 1.dp, if (selected) ui.accent else Color(0x44FFFFFF), CircleShape)
            .combinedClick(onClick = onClick),
    )
}

@Composable
private fun SliderRow(label: String, value: Float, range: ClosedFloatingPointRange<Float>, onChange: (Float) -> Unit) {
    val ui = LocalUi.current
    T(label, 0.88f, modifier = Modifier.padding(top = 4.dp))
    Slider(
        value, onChange, valueRange = range,
        colors = SliderDefaults.colors(thumbColor = ui.accent, activeTrackColor = ui.accent, inactiveTrackColor = Color(0x33FFFFFF)),
    )
}

@Composable
private fun SwitchRow(label: String, value: Boolean, onChange: (Boolean) -> Unit) {
    val ui = LocalUi.current
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
        T(label, 0.88f, modifier = Modifier.weight(1f))
        Switch(value, onChange, colors = SwitchDefaults.colors(checkedTrackColor = ui.accent, checkedThumbColor = Color.White))
    }
}
