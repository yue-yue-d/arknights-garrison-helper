package com.weishu.helper

import android.app.Application
import android.content.Context
import androidx.compose.runtime.mutableStateOf
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import java.io.File

class WeishuApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Store.init(this)
    }
}

// ---------- 设置（文件：设置.json，字段名和桌面版一致的部分可以互相拷贝） ----------
@Serializable
data class Settings(
    @SerialName("主题色") val accent: String = "#41DBB2",
    @SerialName("面板色") val panel: String = "#141A1F",
    @SerialName("悬浮球色") val ballColor: String = "",
    @SerialName("悬浮球图片") val ballImage: String = "图片/通用/奖杯8.png",
    @SerialName("悬浮球大小") val ballSize: Float = 52f,
    @SerialName("贴边隐藏") val edgeHide: Boolean = true,
    @SerialName("宽度") val width: Float = 360f,
    @SerialName("不透明度") val opacity: Float = 0.94f,
    @SerialName("字号") val fontScale: Float = 1f,
    @SerialName("页签") val tab: String = "布阵",
    @SerialName("地图") val map: String = "#01",
    @SerialName("先列后行") val colFirst: Boolean = true,
    @SerialName("球X") val ballX: Int = -1,
    @SerialName("球Y") val ballY: Int = -1,
    @SerialName("面板X") val panelX: Int = 0,
    @SerialName("面板Y") val panelY: Int = 0,
)

// ---------- 布阵存档：布阵/#01.json（每个战场一份，格式和桌面版相同） ----------
@Serializable
class Placed(
    @SerialName("干员") var op: String = "",
    var x: Int = -1,
    var y: Int = -1,
    @SerialName("精锐") var elite: Boolean = false,
    /** 支援（甄选）干员放在哪一阶的甄选槽位：5 或 6；名单干员为 0 */
    @SerialName("等阶") var tier: Int = 0,
)

@Serializable
class Plan(
    @SerialName("名称") var name: String = "方案1",
    @SerialName("场上") val board: MutableList<Placed> = mutableListOf(),
    @SerialName("整备") val bench: MutableList<Placed> = mutableListOf(),
    @SerialName("禁用盟约") val disabled: MutableList<String> = mutableListOf(),
    @SerialName("策略") var strategy: String = "",
    @SerialName("备注") var notes: String = "",
)

@Serializable
class Terrain(
    @SerialName("列") var cols: Int = 12,
    @SerialName("行") var rows: Int = 6,
    @SerialName("格") val cells: MutableList<String> = mutableListOf(),
    /** 网格四个角在截图上的位置（0～1）：左上x,y 右上x,y 右下x,y 左下x,y */
    @SerialName("角点") var corners: MutableList<Double> = mutableListOf(),
) {
    fun corners(): DoubleArray = if (corners.size == 8) corners.toDoubleArray() else doubleArrayOf(0.17, 0.25, 0.97, 0.25, 1.0, 0.80, 0.02, 0.80)
    fun get(x: Int, y: Int): Char = if (y < cells.size && x < cells[y].length) cells[y][x] else '.'
    fun set(x: Int, y: Int, c: Char) {
        fix()
        val a = cells[y].toCharArray()
        a[x] = c
        cells[y] = String(a)
    }

    fun fix() {
        while (cells.size < rows) cells.add("")
        while (cells.size > rows) cells.removeAt(cells.size - 1)
        for (i in 0 until rows) cells[i] = if (cells[i].length >= cols) cells[i].substring(0, cols) else cells[i].padEnd(cols, '.')
    }
}

@Serializable
class SiteFile(
    @SerialName("地形") val terrain: Terrain = Terrain(),
    @SerialName("方案") val plans: MutableList<Plan> = mutableListOf(),
    @SerialName("当前") var current: String = "",
)

object Store {
    lateinit var dir: File
        private set
    private val io = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    val settings = mutableStateOf(Settings())

    fun init(ctx: Context) {
        dir = ctx.filesDir
        GameData.load(ctx)
        val f = File(dir, "设置.json")
        settings.value = runCatching { JsonFmt.decodeFromString(Settings.serializer(), f.readText()) }.getOrDefault(Settings())
        if (GameData.map(settings.value.map) == null && GameData.maps.maps.isNotEmpty())
            settings.value = settings.value.copy(map = GameData.maps.maps.first().id)
    }

    fun update(f: (Settings) -> Settings) {
        val s = f(settings.value)
        if (s == settings.value) return
        settings.value = s
        val text = JsonFmt.encodeToString(Settings.serializer(), s)
        io.launch { writeAtomic(File(dir, "设置.json"), text) }
    }

    fun siteFile(map: String) = File(File(dir, "布阵"), "$map.json")

    fun loadSite(map: String): SiteFile? = runCatching {
        val f = siteFile(map)
        if (f.exists()) JsonFmt.decodeFromString(SiteFile.serializer(), f.readText()) else null
    }.getOrNull()

    fun saveSite(map: String, site: SiteFile) {
        val text = JsonFmt.encodeToString(SiteFile.serializer(), site)
        io.launch { writeAtomic(siteFile(map), text) }
    }

    @Synchronized
    private fun writeAtomic(f: File, text: String) {
        f.parentFile?.mkdirs()
        val tmp = File(f.path + ".tmp")
        tmp.writeText(text)
        if (!tmp.renameTo(f)) { f.delete(); tmp.renameTo(f) }
    }
}
