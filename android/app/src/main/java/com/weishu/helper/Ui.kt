package com.weishu.helper

import android.content.Context
import android.graphics.BitmapFactory
import android.util.LruCache
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.material3.Text
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

// ---------- 颜色 ----------
object Pal {
    val Text = Color(0xFFE8ECEF)
    val Sub = Color(0xFF9AA6AE)
    val Faint = Color(0x66FFFFFF)
    val Card = Color(0x14FFFFFF)
    val Line = Color(0x30FFFFFF)
    val Warn = Color(0xFFF05A4A)
    val Amber = Color(0xFFFFB02E)
    val Core = Color(0xFF7FC8FF)
    val Gold = Color(0xFFF6C60F)
}

val AccentPresets = listOf(
    "薄荷" to "#41DBB2", "罗德岛蓝" to "#3FA9F5", "源石橙" to "#FF9F43", "樱粉" to "#FF7EB6",
    "鸢尾紫" to "#A78BFA", "赤红" to "#F25F5C", "鎏金" to "#F6C60F", "霜白" to "#D8E3EA",
)
val PanelPresets = listOf(
    "夜灰" to "#141A1F", "深海" to "#0E1A2B", "墨黑" to "#0A0A0C", "暖褐" to "#1F1712",
    "暗紫" to "#1A1426", "森绿" to "#0F1C17", "酒红" to "#24121A", "石板" to "#263038",
)

fun hex(s: String?, fallback: Color = Color(0xFF41DBB2)): Color =
    runCatching { Color(android.graphics.Color.parseColor(s)) }.getOrDefault(fallback)

fun Color.mix(o: Color, t: Float) = Color(red + (o.red - red) * t, green + (o.green - green) * t, blue + (o.blue - blue) * t, alpha)

class UiTheme(val accent: Color, val panel: Color, val ball: Color, val font: Float) {
    fun sp(k: Float = 1f): TextUnit = (12.5f * k * font).sp
}

val LocalUi = staticCompositionLocalOf { UiTheme(Color(0xFF41DBB2), Color(0xFF141A1F), Color(0xFF41DBB2), 1f) }

@Composable
fun rememberUiTheme(s: Settings): UiTheme = remember(s.accent, s.panel, s.ballColor, s.fontScale) {
    val a = hex(s.accent)
    UiTheme(a, hex(s.panel, Color(0xFF141A1F)), if (s.ballColor.isBlank()) a else hex(s.ballColor, a), s.fontScale)
}

// ---------- 富文本：**粗体**、【术语】高亮、<条件> 琥珀色 ----------
private val RichRe = Regex("""\*\*(.+?)\*\*|【([^】]+)】|<([^>]+)>""")

fun rich(s: String, accent: Color): AnnotatedString = buildAnnotatedString {
    var last = 0
    for (m in RichRe.findAll(s)) {
        append(s.substring(last, m.range.first))
        when {
            m.groups[1] != null -> withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(m.groups[1]!!.value) }
            m.groups[2] != null -> withStyle(SpanStyle(color = accent, fontWeight = FontWeight.Medium)) { append("【${m.groups[2]!!.value}】") }
            else -> withStyle(SpanStyle(color = Pal.Amber)) { append("<${m.groups[3]!!.value}>") }
        }
        last = m.range.last + 1
    }
    append(s.substring(last))
}

// ---------- 图片（assets/数据/图片/...），带内存缓存 ----------
object AssetImages {
    private val cache = object : LruCache<String, ImageBitmap>(48 * 1024 * 1024) {
        override fun sizeOf(key: String, value: ImageBitmap) = value.width * value.height * 4
    }

    fun peek(rel: String?): ImageBitmap? = if (rel.isNullOrBlank()) null else cache.get(rel)

    fun load(ctx: Context, rel: String?): ImageBitmap? {
        if (rel.isNullOrBlank()) return null
        cache.get(rel)?.let { return it }
        val bmp = runCatching {
            if (rel.startsWith("/")) BitmapFactory.decodeFile(rel)
            else ctx.assets.open("数据/$rel").use { BitmapFactory.decodeStream(it) }
        }.getOrNull() ?: return null
        return bmp.asImageBitmap().also { cache.put(rel, it) }
    }
}

@Composable
fun rememberAsset(rel: String?): ImageBitmap? {
    val ctx = LocalContext.current
    val v by produceState(AssetImages.peek(rel), rel) {
        if (value == null) value = withContext(Dispatchers.IO) { AssetImages.load(ctx, rel) }
    }
    return v
}

@Composable
fun AssetImage(
    rel: String?, modifier: Modifier = Modifier, contentScale: ContentScale = ContentScale.Crop,
    fallback: String? = null, alignment: Alignment = Alignment.Center,
) {
    val bmp = rememberAsset(rel)
    if (bmp != null) Image(bmp, null, modifier, contentScale = contentScale, alignment = alignment)
    else Box(modifier.background(Color(0x22FFFFFF)), contentAlignment = Alignment.Center) {
        if (!fallback.isNullOrEmpty()) Text(fallback.take(2), color = Pal.Text, fontSize = 11.sp, fontWeight = FontWeight.Bold, maxLines = 1)
    }
}

/** 方形或圆形头像，带彩色描边。 */
@Composable
fun Thumb(rel: String?, size: Dp, fallback: String = "", border: Color = Color(0x55FFFFFF), round: Boolean = false, modifier: Modifier = Modifier) {
    val shape = if (round) CircleShape else RoundedCornerShape(5.dp)
    AssetImage(
        rel, modifier.size(size).clip(shape).background(Color(0xE0101418)).border(1.2.dp, border, shape),
        fallback = fallback,
    )
}

// ---------- 通用小部件 ----------
@Composable
fun T(text: String, k: Float = 1f, color: Color = Pal.Text, bold: Boolean = false, modifier: Modifier = Modifier, maxLines: Int = Int.MAX_VALUE) {
    Text(text, modifier, color = color, fontSize = LocalUi.current.sp(k), fontWeight = if (bold) FontWeight.Bold else FontWeight.Normal,
        maxLines = maxLines, overflow = TextOverflow.Ellipsis, lineHeight = LocalUi.current.sp(k * 1.35f))
}

@Composable
fun RichT(text: String, k: Float = 0.95f, color: Color = Pal.Text, modifier: Modifier = Modifier) {
    val ui = LocalUi.current
    Text(rich(text, ui.accent), modifier, color = color, fontSize = ui.sp(k), lineHeight = ui.sp(k * 1.4f))
}

@Composable
fun Section(text: String) = T(text, 0.9f, LocalUi.current.accent, true, Modifier.padding(top = 8.dp, bottom = 4.dp))

/** 圆角小按钮 / 选项。selected 时用主题色。 */
@Composable
fun Chip(
    text: String, selected: Boolean = false, modifier: Modifier = Modifier, color: Color? = null, k: Float = 0.9f,
    enabled: Boolean = true, onLongClick: (() -> Unit)? = null, onClick: () -> Unit,
) {
    val ui = LocalUi.current
    val fg = when {
        !enabled -> Pal.Faint
        selected -> Color(0xFF0E1215)
        else -> color ?: Pal.Text
    }
    val bg = if (selected) ui.accent else Color(0x1AFFFFFF)
    Box(
        modifier
            .padding(end = 4.dp, bottom = 4.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(bg)
            .border(1.dp, if (selected) ui.accent else Color(0x26FFFFFF), RoundedCornerShape(6.dp))
            .combinedClick(enabled, onClick, onLongClick)
            .padding(horizontal = 8.dp, vertical = 4.dp),
    ) { Text(text, color = fg, fontSize = ui.sp(k), fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal, maxLines = 1) }
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun Modifier.combinedClick(enabled: Boolean = true, onClick: () -> Unit, onLongClick: (() -> Unit)? = null): Modifier =
    if (onLongClick == null) this.clickable(enabled = enabled, onClick = onClick)
    else this.then(Modifier.combinedClickable(enabled = enabled, onClick = onClick, onLongClick = onLongClick))

@Composable
fun CardBox(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier
            .fillMaxWidth()
            .padding(bottom = 6.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(Pal.Card)
            .padding(8.dp),
        content = content,
    )
}

/** 左边图片、右边文字的卡片（资料页用）。 */
@Composable
fun ImageCard(img: String?, size: Dp = 46.dp, fallback: String = "", border: Color = Color(0x55FFFFFF), round: Boolean = false, content: @Composable ColumnScope.() -> Unit) {
    CardBox {
        Row {
            Thumb(img, size, fallback, border, round)
            Spacer(Modifier.width(8.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp), content = content)
        }
    }
}

// ---------- 宿主：悬浮窗和普通界面共用面板，靠它区分 ----------
interface PanelHost {
    val overlay: Boolean
    /** 悬浮窗默认不抢焦点（不影响游戏），输入框需要键盘时才临时打开。 */
    fun setFocusable(on: Boolean) {}
    fun collapse() {}
    fun close() {}
    fun toggleClickThrough() {}
    fun movePanel(dx: Float, dy: Float) {}
}

val LocalHost = staticCompositionLocalOf<PanelHost> { object : PanelHost { override val overlay = false } }

/** 悬浮窗外面被点了 / 按了返回键：让输入框交出焦点（计数器每变一次触发一次）。 */
object FocusSignal {
    var clear by mutableStateOf(0)
    /** 正在输入（键盘开着）：悬浮窗面板收起标题栏，给键盘让地方 */
    var typing by mutableStateOf(false)
}

/** 输入框：在悬浮窗里点它时才让窗口可获得焦点并弹出键盘。 */
@Composable
fun InputField(
    value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier,
    singleLine: Boolean = true, minLines: Int = 1,
) {
    val ui = LocalUi.current
    val host = LocalHost.current
    val focus = LocalFocusManager.current
    val kb = LocalSoftwareKeyboardController.current
    var focused by remember { mutableStateOf(false) }
    LaunchedEffect(FocusSignal.clear) { if (focused) focus.clearFocus(true) }
    LaunchedEffect(focused) {
        FocusSignal.typing = focused
        if (focused) { host.setFocusable(true); delay(120); kb?.show() } else host.setFocusable(false)
    }
    BasicTextField(
        value, onChange,
        modifier
            .fillMaxWidth()
            .padding(bottom = 4.dp)
            .clip(RoundedCornerShape(7.dp))
            .background(Color(0x1FFFFFFF))
            .border(1.dp, if (focused) ui.accent else Color(0x26FFFFFF), RoundedCornerShape(7.dp))
            .onFocusChanged { focused = it.isFocused }
            .padding(horizontal = 9.dp, vertical = 7.dp),
        singleLine = singleLine, minLines = minLines,
        textStyle = TextStyle(color = Pal.Text, fontSize = ui.sp(0.95f)),
        cursorBrush = SolidColor(ui.accent),
        decorationBox = { inner ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f)) {
                    if (value.isEmpty()) Text(placeholder, color = Pal.Faint, fontSize = ui.sp(0.95f), maxLines = 1)
                    inner()
                }
                if (value.isNotEmpty() && singleLine)
                    Text("✕", Modifier.padding(start = 6.dp).clickable { onChange("") }, color = Pal.Sub, fontSize = ui.sp(1f))
            }
        },
    )
}

// ---------- 面板内的底部弹层（悬浮窗里不能用系统对话框） ----------
class SheetState {
    var content by mutableStateOf<(@Composable ColumnScope.() -> Unit)?>(null)
    fun show(c: @Composable ColumnScope.() -> Unit) { content = c }
    fun hide() { content = null }
}

val LocalSheet = staticCompositionLocalOf { SheetState() }

@Composable
fun SheetHost(state: SheetState, content: @Composable () -> Unit) {
    val ui = LocalUi.current
    BoxWithConstraints(Modifier.fillMaxSize()) {
        content()
        val c = state.content
        if (c != null) {
            Box(
                Modifier
                    .fillMaxSize()
                    .background(Color(0x99000000))
                    .clickable(remember { MutableInteractionSource() }, null) { state.hide() },
            )
            Column(
                Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .heightIn(max = maxHeight * 0.88f)
                    .clip(RoundedCornerShape(topStart = 14.dp, topEnd = 14.dp))
                    .background(ui.panel.mix(Color.White, 0.06f))
                    .border(BorderStroke(1.dp, ui.accent.copy(alpha = 0.5f)), RoundedCornerShape(topStart = 14.dp, topEnd = 14.dp))
                    .clickable(remember { MutableInteractionSource() }, null) {}
                    .verticalScroll(rememberScrollState())
                    .padding(12.dp),
            ) {
                c()
                Spacer(Modifier.size(4.dp))
                Chip("关闭", modifier = Modifier.align(Alignment.End)) { state.hide() }
            }
        }
    }
}
