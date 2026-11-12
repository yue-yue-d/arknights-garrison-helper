package com.weishu.helper

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp

class RefState {
    var cat by mutableStateOf("干员")
    var search by mutableStateOf("")
    var phaseGroup by mutableStateOf("")
    var phaseSub by mutableStateOf("")
    val opTiers = mutableStateListOf<Int>()
    val opBonds = mutableStateListOf<String>()
    var bondAll by mutableStateOf(false)
    var quickOpen by mutableStateOf(false)
    var itemTier by mutableIntStateOf(0)
}

private val Cats = listOf("干员", "盟约", "策略", "装备", "机变", "领袖", "地图")
private val PhaseGroups = listOf(
    "敌人轮选" to listOf("单只悬赏", "多轮悬赏", "特训", "飞行悬赏", "频次悬赏", "损伤悬赏", "持续悬赏", "隐匿悬赏", "折射悬赏", "特异悬赏"),
    "联合增益/减益" to listOf("盟誓", "驰援", "资源", "战斗效果"),
)

private fun hit(q: String, vararg fields: String?): Boolean {
    val words = q.trim().split(' ').filter { it.isNotBlank() }
    return words.all { w -> fields.any { it != null && it.contains(w, ignoreCase = true) } }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun RefScreen() {
    val rs = Shared.ref
    val ui = LocalUi.current
    val list = rememberLazyListState()
    LaunchedEffect(rs.cat, rs.search, rs.phaseGroup, rs.phaseSub, rs.opTiers.toList(), rs.opBonds.toList(), rs.bondAll, rs.itemTier) { list.scrollToItem(0) }

    // 当前分类下的卡片（最多 200 张）
    val cards: List<@Composable () -> Unit> = remember(rs.cat, rs.search, rs.phaseGroup, rs.phaseSub, rs.opTiers.toList(), rs.opBonds.toList(), rs.bondAll, rs.itemTier) {
        val q = rs.search
        when (rs.cat) {
            "干员" -> GameData.ops.sortedWith(compareBy({ it.tier }, { it.name })).filter { o ->
                (rs.opTiers.isEmpty() || o.tier in rs.opTiers) &&
                    (rs.opBonds.isEmpty() || if (rs.bondAll) rs.opBonds.all { it in o.bonds } else rs.opBonds.any { it in o.bonds }) &&
                    hit(q, o.name, o.bonds.joinToString(" "), o.cls, o.branch, o.pos, o.trait, o.eliteTrait, o.tierText + "阶")
            }.map { o -> @Composable { OpCard(o) } }
            "盟约" -> GameData.bonds.filter { hit(q, it.name, it.type, it.effect, it.members.joinToString(" ")) }.map { b -> @Composable { BondCard(b) } }
            "策略" -> GameData.strategies.filter { hit(q, it.name, it.sponsor, it.effect, it.unlock) }.map { s ->
                @Composable {
                    ImageCard(s.img, 46.dp, s.sponsor) {
                        T(s.name, 1.02f, LocalUi.current.accent, true)
                        T("发起人 ${s.sponsor}　初始生命 ${s.hp}", 0.82f, Pal.Sub)
                        RichT(s.effect, 0.9f)
                        if (s.unlock.isNotEmpty()) T("解锁：${s.unlock}", 0.8f, Pal.Sub)
                    }
                }
            }
            "装备" -> GameData.items.sortedBy { it.tier ?: 0 }.filter { (rs.itemTier == 0 || it.tier == rs.itemTier) && hit(q, it.name, it.effect, it.desc, it.type) }.map { i ->
                @Composable {
                    val tc = Tier.color(i.tier ?: 0)
                    ImageCard(i.img, 46.dp, i.name, tc) {
                        T("${Tier.roman(i.tier ?: 0)}　${i.name}", 1f, tc, true)
                        T("${i.type}　售价 ${i.price ?: "-"}", 0.8f, Pal.Sub)
                        RichT(i.effect, 0.9f)
                        if (i.desc.isNotEmpty()) T(i.desc, 0.78f, Pal.Sub)
                    }
                }
            }
            "机变" -> GameData.phases.filter { (rs.phaseGroup == "" || it.group == rs.phaseGroup) && (rs.phaseSub == "" || it.sub == rs.phaseSub) && hit(q, it.name, it.desc, it.group, it.sub, it.enemy) }.map { p ->
                @Composable {
                    ImageCard(p.img, 46.dp, p.name, round = p.sub == "驰援") {
                        T(p.name, 0.98f, LocalUi.current.accent, true)
                        T("${p.group} · ${p.sub}" + if (p.enemy.isNotEmpty()) "　敌人：${p.enemy}" else "", 0.8f, Pal.Sub)
                        RichT(p.desc, 0.9f)
                    }
                }
            }
            "领袖" -> GameData.leaders.filter { hit(q, it.name) }.map { l -> @Composable { LeaderCard(l) } } +
                listOf(@Composable { T("血量为 PRTS 数据。「隐秘核心」是达成条件后在最终攻势后出现的强化领袖。", 0.8f, Pal.Sub) })
            else -> listOf<@Composable () -> Unit>(@Composable {
                CardBox { T("共通", 1f, LocalUi.current.accent, true); T(GameData.maps.common.joinToString("\n"), 0.9f) }
            }) + GameData.maps.maps.filter { hit(q, it.id, it.notes.joinToString(" "), it.devices.joinToString(" "), it.terrain.joinToString(" ")) }.map { m -> @Composable { MapCard(m) } }
        }
    }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 8.dp), state = list) {
        item {
            FlowRow(Modifier.padding(top = 4.dp)) { for (c in Cats) Chip(c, rs.cat == c) { rs.cat = c } }
            when (rs.cat) {
                "干员" -> OpFilter(rs)
                "装备" -> FlowRow {
                    Chip("全部 ${GameData.items.size}", rs.itemTier == 0, k = 0.82f) { rs.itemTier = 0 }
                    for (t in 1..6) {
                        val n = GameData.items.count { it.tier == t }
                        if (n > 0) Chip("${Tier.roman(t)} $n", rs.itemTier == t, k = 0.82f, color = Tier.color(t)) { rs.itemTier = t }
                    }
                }
                "机变" -> {
                    fun count(g: String, s: String) = GameData.phases.count { (g == "" || it.group == g) && (s == "" || it.sub == s) }
                    FlowRow {
                        Chip("全部 ${count("", "")}", rs.phaseGroup == "", k = 0.82f) { rs.phaseGroup = ""; rs.phaseSub = "" }
                        for ((g, _) in PhaseGroups) Chip("$g ${count(g, "")}", rs.phaseGroup == g, k = 0.82f) { rs.phaseGroup = g; rs.phaseSub = "" }
                    }
                    if (rs.phaseGroup != "") FlowRow(Modifier.padding(start = 10.dp)) {
                        Chip("全部", rs.phaseSub == "", k = 0.78f) { rs.phaseSub = "" }
                        val known = PhaseGroups.first { it.first == rs.phaseGroup }.second
                        val extra = GameData.phases.filter { it.group == rs.phaseGroup }.map { it.sub }.distinct().filter { it !in known }
                        for (s in known + extra) {
                            val n = count(rs.phaseGroup, s)
                            if (n > 0) Chip("$s $n", rs.phaseSub == s, k = 0.78f) { rs.phaseSub = s }
                        }
                    }
                }
            }
            InputField(rs.search, { rs.search = it }, "搜索：名字、盟约、效果里的字…", Modifier.padding(top = 2.dp))
            if (cards.isEmpty()) T("没有找到。", 0.9f, Pal.Sub)
        }
        items(cards.take(200)) { it() }
        item {
            if (cards.size > 200) T("还有 ${cards.size - 200} 条，换个关键词缩小范围。", 0.85f, Pal.Sub)
            Spacer(Modifier.size(24.dp))
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun OpFilter(rs: RefState) {
    val ui = LocalUi.current
    val active = rs.opTiers.size + rs.opBonds.size
    FlowRow {
        Chip((if (rs.quickOpen) "快速检索 ▴" else "快速检索 ▾") + if (active > 0) "（已选 $active）" else "", rs.quickOpen, k = 0.85f) { rs.quickOpen = !rs.quickOpen }
        if (active > 0) Chip("清除", k = 0.82f) { rs.opTiers.clear(); rs.opBonds.clear() }
    }
    if (!rs.quickOpen) return
    Column(Modifier.fillMaxWidth().padding(bottom = 4.dp).clip(RoundedCornerShape(8.dp)).background(Color(0x14FFFFFF)).padding(6.dp)) {
        FlowRow(verticalArrangement = androidx.compose.foundation.layout.Arrangement.Center) {
            T("阶级 ", 0.8f, Pal.Sub, modifier = Modifier.padding(top = 4.dp))
            for (t in 1..6) Chip(Tier.roman(t), t in rs.opTiers, k = 0.85f, color = Tier.color(t)) { if (t in rs.opTiers) rs.opTiers.remove(t) else rs.opTiers.add(t) }
        }
        for (core in listOf(true, false)) {
            FlowRow {
                T(if (core) "核心 " else "附加 ", 0.8f, if (core) Pal.Core else ui.accent, modifier = Modifier.padding(top = 4.dp))
                for (b in GameData.bonds.filter { it.core == core && GameData.ops.any { o -> it.name in o.bonds } })
                    Chip(b.name, b.name in rs.opBonds, k = 0.8f) { if (b.name in rs.opBonds) rs.opBonds.remove(b.name) else rs.opBonds.add(b.name) }
            }
        }
        if (rs.opBonds.size > 1) FlowRow {
            T("多个盟约：", 0.8f, Pal.Sub, modifier = Modifier.padding(top = 4.dp))
            Chip("有其中任一个", !rs.bondAll, k = 0.8f) { rs.bondAll = false }
            Chip("同时都有", rs.bondAll, k = 0.8f) { rs.bondAll = true }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun OpCard(o: Operator) {
    val ui = LocalUi.current
    ImageCard(o.img, 52.dp, o.name, Tier.color(o.tier)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            T(o.tierText + "  ", 1.02f, Tier.color(o.tier), true)
            T(o.name, 1.02f, bold = true)
        }
        T("${o.cls}·${o.branch}　${o.pos}" + if (o.proto.isNotEmpty()) "　原型补位：${o.proto}" else "", 0.8f, Pal.Sub)
        FlowRow {
            for (b in o.bonds) {
                val bd = GameData.bondBy[b]
                Row(
                    Modifier.padding(end = 4.dp, bottom = 3.dp).clip(RoundedCornerShape(4.dp))
                        .background(if (bd?.core == true) Color(0x55337FB5) else Color(0x33FFFFFF))
                        .combinedClick(onClick = { Shared.ref.cat = "盟约"; Shared.ref.search = b }).padding(start = 2.dp, end = 6.dp, top = 1.dp, bottom = 1.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Thumb(bd?.img, 16.dp, b, round = true)
                    Spacer(Modifier.width(3.dp))
                    T(b, 0.82f)
                }
            }
        }
        RichT("**${o.traitType}**　${o.trait}", 0.88f)
        if (o.eliteTrait.isNotEmpty()) RichT("**精锐**　${o.eliteTrait}", 0.88f, Pal.Gold)
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun BondCard(b: Bond) {
    val ui = LocalUi.current
    ImageCard(b.img, 46.dp, b.name, round = true) {
        T(b.name, 1.04f, if (b.core) Pal.Core else ui.accent, true)
        T("${b.type}盟约　激活 ${b.need} 人" + (b.enhance?.takeIf { it != b.need }?.let { "　强化 $it 人" } ?: "") + if (b.benchCounts) "　整备区也计入" else "", 0.8f, Pal.Sub)
        RichT(b.effect, 0.9f)
        if (b.name == "绝技") T("（不需要特定干员：所有精锐状态的干员都算）", 0.8f, Pal.Sub)
        else FlowRow(Modifier.padding(top = 3.dp)) {
            for (o in b.members.mapNotNull { GameData.opBy[it] }.sortedBy { it.tier })
                Thumb(o.img, 30.dp, o.name, Tier.color(o.tier), modifier = Modifier.padding(end = 3.dp, bottom = 3.dp).combinedClick(onClick = { Shared.ref.cat = "干员"; Shared.ref.search = o.name }))
        }
    }
}

@Composable
private fun LeaderCard(l: Leader) {
    var open by remember { mutableStateOf(false) }
    ImageCard(l.img, 54.dp, l.name, Pal.Warn) {
        T(l.name, 1.02f, Pal.Warn, true)
        T("联合模拟", 0.78f, Pal.Sub)
        T(l.hp.entries.joinToString("\n") { "${it.key}：${it.value}" }, 0.86f)
        if (l.soloHp.isNotEmpty()) {
            T("个人模拟（极速 / 规程等级1～20）血量 " + if (open) "▴" else "▾", 0.8f, LocalUi.current.accent,
                modifier = Modifier.padding(top = 3.dp).combinedClick(onClick = { open = !open }))
            if (open) T(l.soloHp.entries.joinToString("\n") { "${it.key}：${it.value}" }, 0.8f, Color(0xFFC9D1D6))
        }
    }
}

@Composable
private fun MapCard(m: MapInfo) {
    val sheet = LocalSheet.current
    val rel = m.images.getOrNull(if (m.images.size >= 3) 1 else 0)
    CardBox {
        T("战场 ${m.id}", 1.02f, LocalUi.current.accent, true)
        T((m.notes + m.devices.map { "专属装置：$it" } + m.terrain.map { "特殊地形：$it" }).joinToString("\n"), 0.86f)
        if (rel != null) AssetImage(
            rel,
            Modifier.padding(top = 5.dp).fillMaxWidth().aspectRatio(910f / 512f).clip(RoundedCornerShape(6.dp))
                .combinedClick(onClick = {
                    sheet.show {
                        T("战场 ${m.id}（场地2）", 1f, LocalUi.current.accent, true)
                        AssetImage(rel, Modifier.padding(top = 6.dp).fillMaxWidth().aspectRatio(910f / 512f), ContentScale.Fit)
                        Chip("在布阵里打开这张图", modifier = Modifier.padding(top = 6.dp)) { sheet.hide(); Shared.planner.switchMap(m.id); Shared.tab = "布阵" }
                    }
                }),
            ContentScale.Crop,
        )
    }
}
