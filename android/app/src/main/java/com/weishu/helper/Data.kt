package com.weishu.helper

import android.content.Context
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

// ---------- 规程数据（和桌面版共用 数据/*.json，由 工具/更新数据.py 生成） ----------

@Serializable
data class Operator(
    @SerialName("名称") val name: String = "",
    @SerialName("等阶") val tier: Int = 0,
    @SerialName("原型") val proto: String = "",
    @SerialName("盟约") val bonds: List<String> = emptyList(),
    @SerialName("特质类型") val traitType: String = "",
    @SerialName("特质") val trait: String = "",
    @SerialName("精锐特质") val eliteTrait: String = "",
    @SerialName("职业") val cls: String = "",
    @SerialName("分支") val branch: String = "",
    @SerialName("位置") val pos: String = "",
    @SerialName("图片") val img: String = "",
    // 支援（甄选）干员才有：类别 = 六星 / 精英干员，阵营 = PRTS 所属国家
    @SerialName("类别") val category: String = "",
    @SerialName("阵营") val faction: String = "",
) {
    val support get() = category.isNotEmpty()
    val melee get() = pos.contains("近战") && !pos.contains("远程")
    val tierText get() = Tier.roman(tier)
}

@Serializable
data class Bond(
    @SerialName("名称") val name: String = "",
    @SerialName("类型") val type: String = "",
    @SerialName("激活人数") val need: Int = 0,
    @SerialName("强化人数") val enhance: Int? = null,
    @SerialName("整备区计入") val benchCounts: Boolean = false,
    @SerialName("效果") val effect: String = "",
    @SerialName("干员") val members: List<String> = emptyList(),
    @SerialName("图片") val img: String = "",
) {
    val core get() = type == "核心"
}

@Serializable
data class Strategy(
    @SerialName("策略") val name: String = "",
    @SerialName("发起人") val sponsor: String = "",
    @SerialName("血量") val hp: Int? = null,
    @SerialName("效果") val effect: String = "",
    @SerialName("解锁条件") val unlock: String = "",
    @SerialName("图片") val img: String = "",
)

@Serializable
data class Item(
    @SerialName("名称") val name: String = "",
    @SerialName("类型") val type: String = "",
    @SerialName("等阶") val tier: Int? = null,
    @SerialName("售价") val price: Int? = null,
    @SerialName("效果") val effect: String = "",
    @SerialName("描述") val desc: String = "",
    @SerialName("图片") val img: String = "",
)

@Serializable
data class Phase(
    @SerialName("分组") val group: String = "",
    @SerialName("子类") val sub: String = "",
    @SerialName("名称") val name: String = "",
    @SerialName("说明") val desc: String = "",
    @SerialName("敌人") val enemy: String = "",
    @SerialName("图片") val img: String = "",
)

@Serializable
data class Leader(
    @SerialName("名称") val name: String = "",
    @SerialName("图片") val img: String = "",
    @SerialName("血量") val hp: Map<String, String> = emptyMap(),
    @SerialName("个人模拟血量") val soloHp: Map<String, String> = emptyMap(),
)

@Serializable
data class MapInfo(
    @SerialName("编号") val id: String = "",
    @SerialName("说明") val notes: List<String> = emptyList(),
    @SerialName("装置") val devices: List<String> = emptyList(),
    @SerialName("地形") val terrain: List<String> = emptyList(),
    @SerialName("场地") val sites: List<Int> = emptyList(),
    @SerialName("图片") val images: List<String> = emptyList(),
)

@Serializable
data class MapsBlock(
    @SerialName("共通") val common: List<String> = emptyList(),
    @SerialName("战场") val maps: List<MapInfo> = emptyList(),
)

@Serializable
data class GridDefault(
    @SerialName("角点") val corners: List<Double> = emptyList(),
    @SerialName("列") val cols: Int = 11,
    @SerialName("行") val rows: Int = 4,
)

@Serializable
data class GridDefaults(@SerialName("战场") val maps: Map<String, GridDefault> = emptyMap())

@Serializable
data class DataFile<T>(val meta: Map<String, String> = emptyMap(), @SerialName("列表") val list: T)

object Tier {
    private val R = arrayOf("?", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ")
    private val C = longArrayOf(0xFF888888, 0xFFC5C5C5, 0xFFFFFFFF, 0xFF46EFBC, 0xFF33C9DA, 0xFFF6C60F, 0xFFE88C1A)
    fun roman(t: Int) = if (t in R.indices) R[t] else "?"
    fun color(t: Int) = androidx.compose.ui.graphics.Color(if (t in C.indices) C[t] else C[0])
}

val JsonFmt = Json {
    ignoreUnknownKeys = true
    isLenient = true
    encodeDefaults = true
    prettyPrint = true
    prettyPrintIndent = "  "
    coerceInputValues = true
}

object GameData {
    var ops: List<Operator> = emptyList(); private set
    var opBy: Map<String, Operator> = emptyMap(); private set
    /** 支援（甄选）干员：名单外的 6★ + 罗德岛特派高级资深干员 */
    var support: List<Operator> = emptyList(); private set
    /** 布阵用：名单干员 + 支援干员 */
    var anyBy: Map<String, Operator> = emptyMap(); private set
    var bonds: List<Bond> = emptyList(); private set
    var bondBy: Map<String, Bond> = emptyMap(); private set
    var strategies: List<Strategy> = emptyList(); private set
    var items: List<Item> = emptyList(); private set
    var phases: List<Phase> = emptyList(); private set
    var leaders: List<Leader> = emptyList(); private set
    var maps: MapsBlock = MapsBlock(); private set
    var grids: Map<String, GridDefault> = emptyMap(); private set
    var season = "卫戍协议·规程"; private set
    var updated = ""; private set
    var deployLimit = 8; private set
    var loaded = false; private set

    private fun read(ctx: Context, name: String) =
        ctx.assets.open("数据/$name").bufferedReader(Charsets.UTF_8).use { it.readText() }

    private fun <T> list(ctx: Context, name: String, s: KSerializer<T>): DataFile<List<T>> =
        JsonFmt.decodeFromString(DataFile.serializer(ListSerializer(s)), read(ctx, "$name.json"))

    @Synchronized
    fun load(ctx: Context) {
        if (loaded) return
        val o = list(ctx, "干员", Operator.serializer())
        ops = o.list
        o.meta["赛季"]?.let { season = it }
        o.meta["更新时间"]?.let { updated = it }
        opBy = ops.groupBy { it.name }.mapValues { it.value.first() }
        support = list(ctx, "支援", Operator.serializer()).list.map { if (it.category.isEmpty()) it.copy(category = "六星") else it }
        anyBy = LinkedHashMap(opBy).also { m -> support.forEach { m.putIfAbsent(it.name, it) } }
        bonds = list(ctx, "盟约", Bond.serializer()).list
        bondBy = bonds.associateBy { it.name }
        strategies = list(ctx, "策略", Strategy.serializer()).list
        items = list(ctx, "装备", Item.serializer()).list
        phases = list(ctx, "机变", Phase.serializer()).list
        leaders = list(ctx, "领袖", Leader.serializer()).list
        maps = JsonFmt.decodeFromString(DataFile.serializer(MapsBlock.serializer()), read(ctx, "地图.json")).list
        grids = JsonFmt.decodeFromString(GridDefaults.serializer(), read(ctx, "网格默认.json")).maps
        deployLimit = maps.common.firstNotNullOfOrNull { Regex("部署上限[：:]\\s*(\\d+)").find(it)?.groupValues?.get(1)?.toInt() } ?: 8
        loaded = true
    }

    fun map(id: String) = maps.maps.firstOrNull { it.id == id }
}
