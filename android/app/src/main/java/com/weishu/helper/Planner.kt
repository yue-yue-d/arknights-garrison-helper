package com.weishu.helper

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.material3.Text
import kotlinx.coroutines.delay
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

// ---------- 盟约计算（和桌面版一致） ----------
data class BondState(val bond: Bond, val count: Int, val next: Int, val state: String, val disabled: Boolean, val extra: String)

object BondCalc {
    fun compute(p: Plan): List<BondState> {
        val any = GameData.anyBy
        val onBoard = p.board.map { it.op }.distinct().filter { it in any }
        val onBench = p.bench.map { it.op }.distinct().filter { it in any }
        val harmony = onBoard.any { any[it]!!.bonds.contains("调和") }
        val res = mutableListOf<BondState>()
        for (b in GameData.bonds) {
            var c: Int
            var extra = ""
            if (b.name == "绝技") c = p.board.count { it.elite }
            else {
                val names = onBoard.filter { any[it]!!.bonds.contains(b.name) }.toMutableSet()
                if (b.benchCounts) onBench.filter { any[it]!!.bonds.contains(b.name) }.forEach { names.add(it) }
                c = names.size
                if (b.core && harmony && c > 0) { c += 1; extra = "（含调和 +1）" }
            }
            if (c == 0) continue
            val t1 = b.need
            val t2 = b.enhance ?: t1
            val state: String
            val next: Int
            if (b.name == "独行") {
                state = if (c == 1) "激活" else "失效"
                next = 1
                if (c > 1) extra = "（场上 2 名以上独行会失效）"
            } else {
                state = if (c >= t2 && t2 > t1) "强化" else if (c >= t1) "激活" else "未激活"
                next = if (c < t1) t1 else if (c < t2) t2 else 0
            }
            res.add(BondState(b, c, next, state, p.disabled.contains(b.name), extra))
        }
        fun rank(s: String) = when (s) { "强化" -> 0; "激活" -> 1; else -> 2 }
        return res.sortedWith(compareBy<BondState> { it.disabled }.thenBy { rank(it.state) }.thenByDescending { it.bond.core }.thenByDescending { it.count })
    }
}

val Tiles = listOf(
    Triple('.', "未标注", 0x22FFFFFFL), Triple('G', "地面", 0xFF5E6E61L), Triple('H', "高台", 0xFF3F79A0L),
    Triple('X', "不可部署", 0xFF151515L), Triple('R', "敌人入口", 0xFFA8382CL), Triple('B', "保护点", 0xFF2C62A8L), Triple('S', "特殊地块", 0xFF7A5BA6L),
)

fun tileColor(c: Char) = Color(Tiles.firstOrNull { it.first == c }?.third ?: 0x22FFFFFFL)
fun tileName(c: Char) = Tiles.firstOrNull { it.first == c }?.second ?: "未标注"

fun canPlace(op: Operator?, t: Char) = when (t) {
    'X', 'R', 'B' -> false
    'H' -> op == null || !op.melee
    else -> true
}

/** 单位正方形 → 四边形的透视变换（Heckbert），q = 左上 右上 右下 左下（0～1）。 */
fun proj(q: DoubleArray, u: Double, v: Double): Pair<Double, Double> {
    val (x0, y0, x1, y1) = listOf(q[0], q[1], q[2], q[3])
    val (x2, y2, x3, y3) = listOf(q[4], q[5], q[6], q[7])
    val dx1 = x1 - x2; val dx2 = x3 - x2; val dx3 = x0 - x1 + x2 - x3
    val dy1 = y1 - y2; val dy2 = y3 - y2; val dy3 = y0 - y1 + y2 - y3
    val a: Double; val b: Double; val c: Double; val d: Double; val e: Double; val f: Double; val g: Double; val h: Double
    if (abs(dx3) < 1e-9 && abs(dy3) < 1e-9) {
        a = x1 - x0; b = x2 - x1; c = x0; d = y1 - y0; e = y2 - y1; f = y0; g = 0.0; h = 0.0
    } else {
        var den = dx1 * dy2 - dx2 * dy1
        if (abs(den) < 1e-12) den = 1e-12
        g = (dx3 * dy2 - dx2 * dy3) / den
        h = (dx1 * dy3 - dx3 * dy1) / den
        a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0
        d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0
    }
    val w = g * u + h * v + 1
    return Pair((a * u + b * v + c) / w, (d * u + e * v + f) / w)
}

// ======================= 布阵状态（悬浮窗和主界面共用一份） =======================
class PlannerState {
    var map by mutableStateOf(Store.settings.value.map); private set
    var site = SiteFile(); private set
    var plan = Plan(); private set
    var rev by mutableIntStateOf(0); private set
    var hold: Placed? = null; private set
    private var holdSrc = ""
    var editTerrain by mutableStateOf(false)
    var brush by mutableStateOf('H')
    var showGrid by mutableStateOf(false)
    var tierFilter by mutableIntStateOf(0)
    var bondFilter by mutableStateOf("")
    var supportMode by mutableStateOf(false)
    var supportCat by mutableStateOf("")
    var search by mutableStateOf("")
    var flash by mutableStateOf("")
    var showBondToggles by mutableStateOf(false)
    val limit get() = GameData.deployLimit

    init { load() }

    private fun refresh() { rev++ }
    private fun changed() { site.current = plan.name; Store.saveSite(map, site); rev++ }
    fun say(s: String) { flash = s }

    private fun applyGridDefault(t: Terrain, size: Boolean) {
        val g = GameData.grids[map] ?: return
        if (g.corners.size == 8) t.corners = g.corners.toMutableList()
        if (size) { t.cols = g.cols; t.rows = g.rows }
        t.fix()
    }

    fun load() {
        val loaded = Store.loadSite(map)
        site = loaded ?: SiteFile()
        if (loaded == null || site.terrain.corners.size != 8)
            applyGridDefault(site.terrain, loaded == null || site.terrain.cells.all { r -> r.all { it == '.' } })
        site.terrain.fix()
        if (site.plans.isEmpty()) site.plans.add(Plan())
        plan = site.plans.firstOrNull { it.name == site.current } ?: site.plans[0]
        hold = null
        refresh()
    }

    fun switchMap(id: String) {
        if (id == map) return
        map = id
        Store.update { it.copy(map = id) }
        load()
    }

    fun selectPlan(p: Plan) { plan = p; hold = null; changed() }

    fun newPlan(copy: Boolean) {
        var n = site.plans.size + 1
        while (site.plans.any { it.name == "方案$n" }) n++
        val p = if (copy) JsonFmt.decodeFromString(Plan.serializer(), JsonFmt.encodeToString(Plan.serializer(), plan)).also { it.name = plan.name + " 副本" }
        else Plan(name = "方案$n")
        site.plans.add(p)
        plan = p
        hold = null
        changed()
    }

    fun rename(name: String) {
        val n = name.trim()
        if (n.isNotEmpty() && site.plans.none { it !== plan && it.name == n }) { plan.name = n; changed() }
    }

    fun deletePlan() {
        site.plans.remove(plan)
        if (site.plans.isEmpty()) site.plans.add(Plan())
        plan = site.plans[0]
        hold = null
        changed()
    }

    fun setNotes(s: String) { plan.notes = s; changed() }
    fun setStrategy(s: String) { plan.strategy = s; changed() }
    fun toggleDisabled(bond: String) { if (!plan.disabled.remove(bond)) plan.disabled.add(bond); changed() }

    fun deployOrder(): List<Placed> =
        if (Store.settings.value.colFirst) plan.board.sortedWith(compareBy({ it.x }, { it.y }))
        else plan.board.sortedWith(compareBy({ it.y }, { it.x }))

    // ---------- 地形 ----------
    fun paint(x: Int, y: Int, c: Char = brush) {
        if (c == '#') return
        if (site.terrain.get(x, y) == c) return
        site.terrain.set(x, y, c)
        changed()
    }

    fun resize(dc: Int, dr: Int) {
        val t = site.terrain
        t.cols = (t.cols + dc).coerceIn(3, 20)
        t.rows = (t.rows + dr).coerceIn(2, 12)
        t.fix()
        plan.board.filter { it.x >= t.cols || it.y >= t.rows }.forEach { plan.board.remove(it); it.x = -1; it.y = -1; plan.bench.add(it) }
        changed()
    }

    fun clearTerrain() {
        val t = site.terrain
        for (y in 0 until t.rows) t.cells[y] = ".".repeat(t.cols)
        changed()
    }

    fun resetGrid() { applyGridDefault(site.terrain, true); resize(0, 0) }

    fun moveCorner(k: Int, nx: Double, ny: Double, save: Boolean) {
        val q = site.terrain.corners()
        q[k * 2] = nx.coerceIn(-0.1, 1.1)
        q[k * 2 + 1] = ny.coerceIn(-0.1, 1.1)
        site.terrain.corners = q.toMutableList()
        if (save) changed() else refresh()
    }

    // ---------- 拿起 / 放下 ----------
    fun cellClick(x: Int, y: Int) {
        if (editTerrain) { if (brush != '#') paint(x, y); return }
        val occ = plan.board.firstOrNull { it.x == x && it.y == y }
        val h = hold
        if (h == null) {
            if (occ != null) { hold = occ; holdSrc = "board"; refresh() }
            return
        }
        if (h === occ) { hold = null; refresh(); return }
        val op = GameData.anyBy[h.op]
        val t = site.terrain.get(x, y)
        if (t == 'X' || t == 'R' || t == 'B') { say("这一格不能部署"); return }
        if (t == 'H' && op != null && op.melee) { say("${op.name} 是近战位，不能放在高台上"); return }
        if (holdSrc != "board" && occ == null && plan.board.size >= limit) { say("场上已经 $limit 人了（部署上限）"); return }
        if (occ != null) {
            if (holdSrc == "board") { occ.x = h.x; occ.y = h.y } else { plan.board.remove(occ); occ.x = -1; occ.y = -1; plan.bench.add(occ) }
        }
        if (holdSrc == "bench") plan.bench.remove(h)
        h.x = x; h.y = y
        if (!plan.board.contains(h)) plan.board.add(h)
        hold = null
        changed()
    }

    fun benchClick(p: Placed) {
        hold = if (hold === p) null else p
        holdSrc = "bench"
        refresh()
    }

    /** 拿着场上的干员时点整备区：放回整备区。 */
    fun dropToBench() {
        val h = hold ?: return
        if (holdSrc != "board") return
        plan.board.remove(h); h.x = -1; h.y = -1; plan.bench.add(h)
        hold = null
        changed()
    }

    fun cancelHold() { if (hold != null) { hold = null; refresh() } }

    fun pickUp(o: Operator) {
        editTerrain = false
        var tier = 0
        if (o.support) {
            tier = existingTier(o.name) ?: if (canSelect(o.name, 6)) 6 else if (canSelect(o.name, 5)) 5 else 0
            if (tier == 0) { say("Ⅴ、Ⅵ阶的甄选槽位都满了"); return }
        }
        hold = Placed(op = o.name, tier = tier)
        holdSrc = "new"
        say(if (o.support) "拿起了 ${o.name}（${Tier.roman(tier)} 阶甄选），点地图上的格子放下" else "拿起了 ${o.name}，点地图上的格子放下")
        refresh()
    }

    fun addToBench(o: Operator, tier: Int = 0) {
        if (o.support && !canSelect(o.name, tier)) { say(selectBlockReason(o.name, tier)); return }
        plan.bench.add(Placed(op = o.name, tier = tier))
        changed()
    }

    fun toggleElite(p: Placed) { p.elite = !p.elite; changed() }
    fun toBench(p: Placed) { plan.board.remove(p); p.x = -1; p.y = -1; plan.bench.add(p); if (hold === p) hold = null; changed() }
    fun remove(p: Placed) { plan.board.remove(p); plan.bench.remove(p); if (hold === p) hold = null; changed() }

    fun switchSelectTier(p: Placed) {
        val other = if (p.tier == 5) 6 else 5
        if (!canSelect(p.op, other, ignoreSelf = true)) { say("${Tier.roman(other)} 阶的 2 个甄选槽位已经满了"); return }
        (plan.board + plan.bench).filter { it.op == p.op }.forEach { it.tier = other }
        changed()
    }

    // ---------- 甄选槽位：Ⅴ、Ⅵ阶各 2 个 ----------
    private fun allPlaced() = plan.board + plan.bench
    private fun isSupport(name: String) = GameData.anyBy[name]?.support == true
    fun selCount(tier: Int) = allPlaced().filter { it.tier == tier && isSupport(it.op) }.map { it.op }.distinct().size
    fun existingTier(name: String): Int? = allPlaced().firstOrNull { it.op == name && it.tier > 0 }?.tier
    fun canSelect(name: String, tier: Int, ignoreSelf: Boolean = false): Boolean {
        val cur = existingTier(name)
        if (cur != null && !ignoreSelf) return cur == tier
        return allPlaced().filter { it.tier == tier && isSupport(it.op) && it.op != name }.map { it.op }.distinct().size < 2
    }

    fun selectBlockReason(name: String, tier: Int): String {
        val cur = existingTier(name)
        return if (cur != null && cur != tier) "$name 已经放在 ${Tier.roman(cur)} 阶甄选槽位了（长按头像可以改）" else "${Tier.roman(tier)} 阶的 2 个甄选槽位已经满了"
    }

    /** 本局盟约没开时，干员会不会出现。 */
    fun available(o: Operator): Boolean {
        val off = plan.disabled
        val core = o.bonds.filter { GameData.bondBy[it]?.core == true }
        val extra = o.bonds.filter { it !in core }
        if (core.any { it in off }) return false
        if (extra.isNotEmpty() && core.isEmpty() && extra.all { it in off }) return false
        return true
    }

    fun pickerList(): Pair<List<Operator>, Int> {
        val words = search.trim().split(' ').filter { it.isNotBlank() }
        var hidden = 0
        val src = if (supportMode) GameData.support.filter { supportCat == "" || it.category == supportCat }
        else GameData.ops.filter { tierFilter == 0 || it.tier == tierFilter }.sortedWith(compareBy({ it.tier }, { it.name }))
        val out = src.filter { o ->
            (bondFilter == "" || o.bonds.contains(bondFilter)) &&
                words.all { w ->
                    o.name.contains(w, true) || o.bonds.any { it.contains(w) } || o.cls.contains(w) || o.branch.contains(w) ||
                        o.pos.contains(w) || o.trait.contains(w) || o.faction.contains(w)
                }
        }.filter { if (available(it)) true else { hidden++; false } }
        return out to hidden
    }
}

object Shared {
    val planner by lazy { PlannerState() }
    val ref by lazy { RefState() }
    var tab by mutableStateOf(Store.settings.value.tab)
    var showSettings by mutableStateOf(false)

    fun showRef(cat: String, query: String) {
        ref.cat = cat
        ref.search = query
        tab = "资料"
        showSettings = false
    }
}

// ======================= 布阵页 =======================
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun PlannerScreen() {
    val st = Shared.planner
    @Suppress("UNUSED_VARIABLE") val rev = st.rev
    val ui = LocalUi.current
    val sheet = LocalSheet.current
    val (picks, hidden) = st.pickerList()

    LaunchedEffect(st.flash) { if (st.flash.isNotEmpty()) { delay(3000); st.flash = "" } }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 8.dp)) {
        item {
            st.rev
            FlowRow(Modifier.padding(top = 4.dp)) {
                for (m in GameData.maps.maps) Chip(m.id, m.id == st.map, k = 0.82f) { st.switchMap(m.id) }
            }
            FlowRow {
                Chip("方案：${st.plan.name} ▾", k = 0.85f) { sheet.show { PlanSheet(st, sheet) } }
                Chip("编辑地形", st.editTerrain, k = 0.85f) { st.editTerrain = !st.editTerrain; st.cancelHold() }
                Chip("显示格子", st.showGrid, k = 0.85f) { st.showGrid = !st.showGrid }
            }
            if (st.editTerrain) TerrainPalette(st)
            Board(st)
            if (st.flash.isNotEmpty()) T(st.flash, 0.92f, Pal.Amber, true, Modifier.padding(top = 2.dp))
            StatusLine(st)
            Section("盟约")
            BondsRow(st)
            Section("整备区")
            BenchRow(st)
            Section("策略")
            val s = GameData.strategies.firstOrNull { it.name == st.plan.strategy }
            Chip(if (s == null) "（未选）点这里选择开局策略 ▾" else "${s.name}　${s.sponsor} · ${s.hp}血 ▾", k = 0.88f) {
                sheet.show { StrategySheet(st, sheet) }
            }
            if (s != null) RichT(s.effect, 0.85f, modifier = Modifier.padding(bottom = 4.dp))
            Chip(if (st.showBondToggles) "本局可用盟约 ▴" else "本局可用盟约 ▾", st.showBondToggles, k = 0.85f) { st.showBondToggles = !st.showBondToggles }
            if (st.showBondToggles) {
                FlowRow {
                    for (b in GameData.bonds) {
                        val off = st.plan.disabled.contains(b.name)
                        Chip(if (off) "${b.name}✕" else b.name, k = 0.8f, color = if (off) Pal.Faint else if (b.core) Pal.Core else ui.accent) { st.toggleDisabled(b.name) }
                    }
                }
                T("核心盟约没开，它的干员就不会出现；附加盟约全灰的干员也不会出现。", 0.78f, Pal.Sub)
            }
            Row(Modifier.padding(top = 8.dp)) {
                Chip("干员", !st.supportMode) { st.supportMode = false }
                Chip("支援", st.supportMode) { st.supportMode = true }
            }
            T(
                if (st.supportMode) "甄选槽位　Ⅴ阶 ${st.selCount(5)}/2　Ⅵ阶 ${st.selCount(6)}/2　点「Ⅴ」「Ⅵ」放进整备区，点一下整行直接拿起。甄选干员没有特质。"
                else "点一下拿起，再点地图格子放下；＋ 直接放进整备区。长按场上头像：精锐 / 放回 / 移除。",
                0.78f, Pal.Sub,
            )
            InputField(st.search, { st.search = it }, "搜名字 / 盟约 / 职业 / 特质…", Modifier.padding(top = 4.dp))
            FlowRow {
                if (st.supportMode) {
                    for ((label, cat) in listOf("全部" to "", "六星" to "六星", "精英干员" to "精英干员"))
                        Chip(label, st.supportCat == cat, k = 0.82f) { st.supportCat = cat }
                } else {
                    Chip("全部", st.tierFilter == 0, k = 0.82f) { st.tierFilter = 0 }
                    for (t in 1..6) Chip(Tier.roman(t), st.tierFilter == t, k = 0.82f, color = Tier.color(t)) { st.tierFilter = t }
                }
                if (st.bondFilter.isNotEmpty()) Chip("只看：${st.bondFilter} ✕", true, k = 0.82f) { st.bondFilter = "" }
            }
        }
        items(picks, key = { (if (it.support) "s:" else "o:") + it.name }) { o -> PickerRow(st, o) }
        item {
            st.rev
            if (picks.isEmpty()) T("没有符合条件的干员", 0.85f, Pal.Sub)
            if (hidden > 0) T("另有 $hidden 人因为本局盟约没开被隐藏", 0.78f, Pal.Sub, modifier = Modifier.padding(vertical = 4.dp))
            Section("备注")
            InputField(st.plan.notes, { st.setNotes(it) }, "这局的想法、要找的人…", singleLine = false, minLines = 3)
            Spacer(Modifier.size(24.dp))
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun TerrainPalette(st: PlannerState) {
    st.rev   // 读一下变更计数：方案改了就重组
    FlowRow {
        Chip("对齐网格", st.brush == '#', k = 0.8f, color = Pal.Amber) { st.brush = '#' }
        for ((c, name, _) in Tiles) Chip(name, st.brush == c, k = 0.8f, color = tileColor(c).mix(Color.White, 0.35f)) { st.brush = c }
        Chip("列−", k = 0.8f) { st.resize(-1, 0) }
        Chip("列＋", k = 0.8f) { st.resize(1, 0) }
        Chip("行−", k = 0.8f) { st.resize(0, -1) }
        Chip("行＋", k = 0.8f) { st.resize(0, 1) }
        Chip("清空", k = 0.8f) { st.clearTerrain() }
        Chip("恢复默认网格", k = 0.8f) { st.resetGrid() }
    }
    T(
        if (st.brush == '#') "拖动四个黄色圆点，对准地图上格子区域的四个角。"
        else "点或拖动涂格子；选「未标注」当橡皮擦。网格已经按截图对好了，没对准时用「对齐网格」。地形是这张图所有方案共用的。",
        0.76f, Pal.Sub, modifier = Modifier.padding(bottom = 2.dp),
    )
}

/** 地图截图 + 透视网格 + 场上干员头像。 */
@Composable
private fun Board(st: PlannerState) {
    @Suppress("UNUSED_VARIABLE") val rev = st.rev
    val ui = LocalUi.current
    val sheet = LocalSheet.current
    val rel = remember(st.map) {
        GameData.map(st.map)?.images?.let { imgs -> imgs.getOrNull(if (imgs.size >= 3) 1 else 0) }
    }
    val bmp = rememberAsset(rel)
    val density = LocalDensity.current
    BoxWithConstraints(Modifier.fillMaxWidth().padding(vertical = 3.dp)) {
        val wDp = if (maxWidth > 560.dp) 560.dp else maxWidth   // 横屏主界面里别铺满整屏
        val aspect = bmp?.let { it.width.toFloat() / it.height } ?: (910f / 512f)
        val hDp = wDp / aspect
        val bw = with(density) { wDp.toPx() }
        val bh = with(density) { hDp.toPx() }
        val t = st.site.terrain
        val held = st.hold?.let { GameData.anyBy[it.op] }
        val align = st.editTerrain && st.brush == '#'

        fun pt(q: DoubleArray, u: Double, v: Double): Offset = proj(q, u, v).let { Offset((it.first * bw).toFloat(), (it.second * bh).toFloat()) }
        fun cellPoly(q: DoubleArray, x: Int, y: Int) = listOf(
            pt(q, x.toDouble() / t.cols, y.toDouble() / t.rows), pt(q, (x + 1.0) / t.cols, y.toDouble() / t.rows),
            pt(q, (x + 1.0) / t.cols, (y + 1.0) / t.rows), pt(q, x.toDouble() / t.cols, (y + 1.0) / t.rows),
        )
        fun inside(p: Offset, poly: List<Offset>): Boolean {
            var c = false
            var j = poly.size - 1
            for (i in poly.indices) {
                val a = poly[i]; val b = poly[j]
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) c = !c
                j = i
            }
            return c
        }
        fun hit(p: Offset): Pair<Int, Int>? {
            val q = t.corners()
            for (y in 0 until t.rows) for (x in 0 until t.cols) if (inside(p, cellPoly(q, x, y))) return x to y
            return null
        }

        Box(
            Modifier
                .size(wDp, hDp)
                .clip(RoundedCornerShape(6.dp))
                .clipToBounds()
                .background(Color(0xFF0B0E11)),
        ) {
            if (bmp != null) Image(bmp, null, Modifier.fillMaxSize(), contentScale = ContentScale.FillBounds)
            Canvas(
                Modifier
                    .fillMaxSize()
                    .pointerInput(st.map, align, st.editTerrain) {
                        detectTapGestures(
                            onTap = { p -> if (!align) hit(p)?.let { (x, y) -> st.cellClick(x, y) } },
                            onLongPress = { p ->
                                if (st.editTerrain) return@detectTapGestures
                                val c = hit(p) ?: return@detectTapGestures
                                val occ = st.plan.board.firstOrNull { it.x == c.first && it.y == c.second }
                                if (occ != null) sheet.show { PlacedMenu(st, occ, true, sheet) } else st.cancelHold()
                            },
                        )
                    }
                    .pointerInput(st.map, align, st.editTerrain, st.brush) {
                        if (!st.editTerrain) return@pointerInput
                        var corner = -1
                        var start = Offset.Zero
                        var q0 = 0.0 to 0.0
                        var acc = Offset.Zero
                        detectDragGestures(
                            onDragStart = { p ->
                                if (align) {
                                    val q = t.corners()
                                    corner = (0 until 4).minByOrNull { k ->
                                        val hx = (q[k * 2] * bw).coerceIn(8.0, bw - 8.0); val hy = (q[k * 2 + 1] * bh).coerceIn(8.0, bh - 8.0)
                                        hypot(hx - p.x, hy - p.y)
                                    } ?: -1
                                    start = p; acc = Offset.Zero
                                    if (corner >= 0) q0 = q[corner * 2] to q[corner * 2 + 1]
                                } else hit(p)?.let { (x, y) -> st.paint(x, y) }
                            },
                            onDrag = { change, d ->
                                change.consume()
                                if (align) {
                                    if (corner < 0) return@detectDragGestures
                                    acc += d
                                    st.moveCorner(corner, q0.first + acc.x / bw, q0.second + acc.y / bh, false)
                                } else hit(change.position)?.let { (x, y) -> st.paint(x, y) }
                            },
                            onDragEnd = { if (align && corner >= 0) st.moveCorner(corner, t.corners()[corner * 2], t.corners()[corner * 2 + 1], true); corner = -1 },
                        )
                    },
            ) {
                val q = t.corners()
                for (y in 0 until t.rows) for (x in 0 until t.cols) {
                    val c = t.get(x, y)
                    val occ = st.plan.board.any { it.x == x && it.y == y }
                    var fill = Color.Transparent
                    var stroke = Color.Transparent
                    var sw = 0f
                    if (st.editTerrain) {
                        if (c != '.') fill = tileColor(c).copy(alpha = 0.6f)
                        stroke = if (align) Color(0xCCFFB02E) else Color(0x99FFFFFF); sw = if (align) 1.2f else 0.8f
                    } else {
                        if (st.showGrid && c != '.') fill = tileColor(c).copy(alpha = 0.33f)
                        if (st.showGrid) { stroke = Color(0x55FFFFFF); sw = 0.6f }
                        if (st.hold != null && c != '.' && canPlace(held, c) && !occ) { fill = ui.accent.copy(alpha = 0.2f); stroke = ui.accent.copy(alpha = 0.67f); sw = 1.2f }
                    }
                    if (fill.alpha == 0f && sw == 0f) continue
                    val poly = cellPoly(q, x, y)
                    val path = Path().apply { moveTo(poly[0].x, poly[0].y); poly.drop(1).forEach { lineTo(it.x, it.y) }; close() }
                    if (fill.alpha > 0f) drawPath(path, fill, style = Fill)
                    if (sw > 0f) drawPath(path, stroke, style = Stroke(sw * density.density))
                }
                if (align) {
                    val frame = Path().apply {
                        val a = pt(q, 0.0, 0.0); moveTo(a.x, a.y)
                        listOf(pt(q, 1.0, 0.0), pt(q, 1.0, 1.0), pt(q, 0.0, 1.0)).forEach { lineTo(it.x, it.y) }; close()
                    }
                    drawPath(frame, Color(0xFFFFB02E), style = Stroke(1.6f * density.density))
                    for (k in 0 until 4) {
                        val hx = (q[k * 2] * bw).toFloat().coerceIn(8f, bw - 8f)
                        val hy = (q[k * 2 + 1] * bh).toFloat().coerceIn(8f, bh - 8f)
                        drawCircle(Color(0xEEFFB02E), 9f * density.density, Offset(hx, hy))
                        drawCircle(Color.Black, 9f * density.density, Offset(hx, hy), style = Stroke(1.2f * density.density))
                    }
                }
            }
            // 场上干员头像（不拦截点击，点击由下面的网格处理）
            val q = t.corners()
            val order = st.deployOrder()
            for (p in st.plan.board) {
                if (p.x < 0 || p.x >= t.cols || p.y < 0 || p.y >= t.rows) continue
                val um = (p.x + 0.5) / t.cols; val vm = (p.y + 0.5) / t.rows
                val c = pt(q, um, vm)
                val l = pt(q, p.x.toDouble() / t.cols, vm); val r = pt(q, (p.x + 1.0) / t.cols, vm)
                val u = pt(q, um, p.y.toDouble() / t.rows); val d = pt(q, um, (p.y + 1.0) / t.rows)
                val size = max(14f, min((r - l).getDistance(), (d - u).getDistance() * 1.15f) * 0.92f)
                val sDp = with(density) { size.toDp() }
                val o = GameData.anyBy[p.op]
                Box(Modifier.offset { IntOffset((c.x - size / 2).roundToInt(), (c.y - size / 2).roundToInt()) }) {
                    Avatar(p, sDp, order.indexOf(p) + 1, p === st.hold, o != null && t.get(p.x, p.y) == 'H' && o.melee, dim = st.editTerrain)
                }
            }
        }
    }
}

/** 干员头像：等阶色边框、左上部署顺序、右上精锐★、右下「甄」。 */
@Composable
fun Avatar(p: Placed, size: Dp, order: Int, held: Boolean, wrong: Boolean, dim: Boolean = false) {
    val ui = LocalUi.current
    val o = GameData.anyBy[p.op]
    val tier = if (p.tier > 0) p.tier else o?.tier ?: 0
    val border = when { wrong -> Pal.Warn; held -> ui.accent; else -> Tier.color(tier) }
    val small = size < 30.dp
    Box(Modifier.size(size)) {
        AssetImage(
            o?.img, Modifier.fillMaxSize().clip(RoundedCornerShape(4.dp)).background(Color(0xE0101418))
                .border(if (wrong || held) 2.dp else 1.4.dp, border, RoundedCornerShape(4.dp)),
            fallback = p.op,
        )
        if (held) Box(Modifier.fillMaxSize().clip(RoundedCornerShape(4.dp)).background(ui.accent.copy(alpha = 0.33f)))
        if (dim) Box(Modifier.fillMaxSize().background(Color(0x88000000)))
        if (order > 0) Box(Modifier.align(Alignment.TopStart).padding(1.dp).background(Color(0xC0000000), RoundedCornerShape(bottomEnd = 4.dp)).padding(horizontal = 2.dp)) {
            Text("$order", color = Pal.Amber, fontSize = ui.sp(if (small) 0.6f else 0.72f), fontWeight = FontWeight.Bold)
        }
        if (p.elite) Text("★", Modifier.align(Alignment.TopEnd).padding(end = 1.dp), color = Pal.Gold, fontSize = ui.sp(if (small) 0.65f else 0.8f))
        if (o?.support == true) Box(Modifier.align(Alignment.BottomEnd).padding(1.dp).background(Color(0xD0000000), RoundedCornerShape(topStart = 4.dp)).padding(horizontal = 2.dp)) {
            Text("甄", color = Tier.color(tier), fontSize = ui.sp(0.6f), fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun StatusLine(st: PlannerState) {
    st.rev   // 读一下变更计数：方案改了就重组
    val ui = LocalUi.current
    val order = st.deployOrder()
    val active = BondCalc.compute(st.plan).count { !it.disabled && (it.state == "激活" || it.state == "强化") }
    Row(Modifier.padding(top = 2.dp)) {
        T("场上 ${st.plan.board.size}/${st.limit}", 0.95f, if (st.plan.board.size > st.limit) Pal.Warn else Pal.Text, true)
        T("　整备 ${st.plan.bench.size}　激活盟约 $active 个", 0.95f)
    }
    val wrong = st.plan.board.filter { GameData.anyBy[it.op]?.melee == true && st.site.terrain.get(it.x, it.y) == 'H' }.map { it.op }
    if (wrong.isNotEmpty()) T("近战放在了高台：${wrong.joinToString("、")}", 0.85f, Pal.Warn)
    val h = st.hold?.let { GameData.anyBy[it.op] }
    when {
        h != null -> Row {
            T("手上：${h.tierText} ${h.name}（${h.pos}）→ 点格子放下", 0.85f, ui.accent, modifier = Modifier.weight(1f))
            Chip("取消", k = 0.78f) { st.cancelHold() }
        }
        st.editTerrain -> {}
        order.isNotEmpty() -> T("部署顺序：" + order.joinToString(" → ") { it.op }, 0.8f, Pal.Sub)
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun BondsRow(st: PlannerState) {
    st.rev   // 读一下变更计数：方案改了就重组
    val sheet = LocalSheet.current
    val ui = LocalUi.current
    val list = BondCalc.compute(st.plan)
    if (list.isEmpty()) { T("场上还没有干员", 0.85f, Pal.Faint); return }
    FlowRow {
        for (s in list) {
            val bg = if (s.disabled) Color(0x40F05A4A) else when (s.state) { "强化" -> Color(0xB0B08A1A); "激活" -> Color(0x9020906E); "失效" -> Color(0x40F05A4A); else -> Color(0x26FFFFFF) }
            val txt = s.bond.name + " " + if (s.next > 0) "${s.count}/${s.next}" else "${s.count} 满"
            Box(
                Modifier.padding(end = 4.dp, bottom = 4.dp).clip(RoundedCornerShape(4.dp)).background(bg)
                    .border(1.dp, if (s.bond.core) Pal.Core.copy(alpha = 0.7f) else Color.Transparent, RoundedCornerShape(4.dp))
                    .combinedClick(onClick = { sheet.show { BondInfo(st, s, sheet) } })
                    .padding(horizontal = 6.dp, vertical = 2.dp),
            ) {
                Text(txt, color = if (s.state == "未激活") Color(0xB0FFFFFF) else Color.White, fontSize = ui.sp(0.88f),
                    fontWeight = if (s.bond.core) FontWeight.Bold else FontWeight.Normal,
                    textDecoration = if (s.disabled) androidx.compose.ui.text.style.TextDecoration.LineThrough else null)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun BondInfo(st: PlannerState, s: BondState, sheet: SheetState) {
    val b = s.bond
    val ui = LocalUi.current
    Row(verticalAlignment = Alignment.CenterVertically) {
        Thumb(b.img, 40.dp, b.name, round = true)
        Spacer(Modifier.width(8.dp))
        Column {
            T("【${b.name}】${b.type}盟约　${s.state}${s.extra}${if (s.disabled) "（本局禁用）" else ""}", 1f, if (b.core) Pal.Core else ui.accent, true)
            T("激活 ${b.need} 人" + (b.enhance?.takeIf { it != b.need }?.let { "，强化 $it 人" } ?: "") + if (b.benchCounts) "，整备区也计入" else "", 0.85f, Pal.Sub)
        }
    }
    RichT(b.effect, 0.92f, modifier = Modifier.padding(vertical = 6.dp))
    val owned = (st.plan.board.map { it.op } + if (b.benchCounts) st.plan.bench.map { it.op } else emptyList()).toSet()
    FlowRow {
        for (o in b.members.mapNotNull { GameData.anyBy[it] }.sortedBy { it.tier }) {
            Column(Modifier.padding(end = 4.dp, bottom = 4.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Thumb(o.img, 36.dp, o.name, if (o.name in owned) ui.accent else Tier.color(o.tier))
                T((if (o.name in owned) "✓" else "") + o.name, 0.68f, if (o.name in owned) ui.accent else Pal.Sub, maxLines = 1)
            }
        }
    }
    Chip("只看这个盟约的干员", k = 0.88f) { st.bondFilter = b.name; sheet.hide() }
}

@OptIn(ExperimentalLayoutApi::class, ExperimentalFoundationApi::class)
@Composable
private fun BenchRow(st: PlannerState) {
    st.rev   // 读一下变更计数：方案改了就重组
    val sheet = LocalSheet.current
    val ui = LocalUi.current
    Column(
        Modifier.fillMaxWidth().heightIn(min = 40.dp).clip(RoundedCornerShape(6.dp)).background(Color(0x14FFFFFF))
            .combinedClick(onClick = { st.dropToBench() }).padding(4.dp),
    ) {
        if (st.hold != null && st.plan.board.contains(st.hold)) T("点这里：把手上的干员放回整备区", 0.8f, ui.accent)
        if (st.plan.bench.isEmpty()) T("空（＋ 或「Ⅴ/Ⅵ」把干员放进来）", 0.8f, Pal.Faint)
        FlowRow {
            for (p in st.plan.bench) {
                Box(Modifier.padding(end = 4.dp, bottom = 4.dp).combinedClick(onClick = { st.benchClick(p) }, onLongClick = { sheet.show { PlacedMenu(st, p, false, sheet) } })) {
                    Avatar(p, 40.dp, 0, p === st.hold, false)
                }
            }
        }
    }
}

/** 长按头像的菜单。 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PlacedMenu(st: PlannerState, p: Placed, onBoard: Boolean, sheet: SheetState) {
    val o = GameData.anyBy[p.op]
    val ui = LocalUi.current
    Row(verticalAlignment = Alignment.CenterVertically) {
        Avatar(p, 44.dp, 0, false, false)
        Spacer(Modifier.width(8.dp))
        Column {
            if (o == null) T(p.op, 1f, bold = true)
            else {
                T(if (o.support) "${Tier.roman(p.tier)} ${o.name}（甄选 · ${o.category}）" else "${o.tierText} ${o.name}", 1f, Tier.color(if (p.tier > 0) p.tier else o.tier), true)
                T("${o.cls}·${o.branch}　${o.pos}" + if (o.support) "　阵营：${o.faction.ifEmpty { "无" }}" else "", 0.82f, Pal.Sub)
                T("盟约：${o.bonds.joinToString("、")}", 0.82f, ui.accent)
            }
        }
    }
    if (o != null && !o.support) RichT("**${o.traitType}**　${if (p.elite && o.eliteTrait.isNotEmpty()) o.eliteTrait else o.trait}", 0.85f, modifier = Modifier.padding(vertical = 4.dp))
    if (o?.support == true) T("甄选干员没有特质", 0.8f, Pal.Sub)
    androidx.compose.foundation.layout.FlowRow(Modifier.padding(top = 6.dp)) {
        Chip(if (p.elite) "取消精锐 ★" else "设为精锐 ★") { st.toggleElite(p); sheet.hide() }
        if (onBoard) Chip("放回整备区") { st.toBench(p); sheet.hide() }
        Chip("移除", color = Pal.Warn) { st.remove(p); sheet.hide() }
        if (o?.support == true) Chip("改放到 ${Tier.roman(if (p.tier == 5) 6 else 5)} 阶甄选槽位") { st.switchSelectTier(p); sheet.hide() }
        else if (o != null) Chip("在资料页查看") { sheet.hide(); Shared.showRef("干员", o.name) }
    }
}

@Composable
private fun PickerRow(st: PlannerState, o: Operator) {
    st.rev   // 读一下变更计数：方案改了就重组
    val ui = LocalUi.current
    Row(
        Modifier.fillMaxWidth().padding(vertical = 1.dp).clip(RoundedCornerShape(6.dp))
            .combinedClick(onClick = { st.pickUp(o) }).padding(vertical = 3.dp, horizontal = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Thumb(o.img, 32.dp, o.name, if (o.support) Pal.Gold else Tier.color(o.tier))
        Spacer(Modifier.width(7.dp))
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (o.support) T(if (o.category == "精英干员") "精英 " else "6★ ", 0.8f, if (o.category == "精英干员") Color(0xFFE88C1A) else Pal.Gold, true)
                else T(o.tierText + " ", 0.95f, Tier.color(o.tier), true)
                T(o.name, 0.95f, bold = true, maxLines = 1)
                T("  ${o.cls}${if (o.melee) "·近" else "·远"}", 0.78f, Pal.Sub, maxLines = 1)
            }
            Row {
                for (b in o.bonds) T("$b ", 0.78f, if (GameData.bondBy[b]?.core == true) Pal.Core else ui.accent, maxLines = 1)
            }
        }
        if (o.support) {
            for (t in listOf(5, 6)) Chip(Tier.roman(t), k = 0.85f, color = Tier.color(t), enabled = st.canSelect(o.name, t)) { st.addToBench(o, t) }
        } else Chip("＋", k = 0.9f) { st.addToBench(o) }
    }
}

@Composable
private fun StrategySheet(st: PlannerState, sheet: SheetState) {
    st.rev   // 读一下变更计数：方案改了就重组
    val ui = LocalUi.current
    T("选择开局策略", 1f, ui.accent, true)
    Chip("（不选）", st.plan.strategy.isEmpty(), Modifier.padding(top = 6.dp)) { st.setStrategy(""); sheet.hide() }
    for (s in GameData.strategies) {
        Row(
            Modifier.fillMaxWidth().padding(vertical = 2.dp).clip(RoundedCornerShape(8.dp))
                .background(if (s.name == st.plan.strategy) ui.accent.copy(alpha = 0.18f) else Pal.Card)
                .combinedClick(onClick = { st.setStrategy(s.name); sheet.hide() }).padding(6.dp),
        ) {
            Thumb(s.img, 38.dp, s.sponsor)
            Spacer(Modifier.width(8.dp))
            Column(Modifier.weight(1f)) {
                T("${s.name}　${s.sponsor} · 初始生命 ${s.hp}", 0.9f, ui.accent, true)
                RichT(s.effect, 0.82f)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PlanSheet(st: PlannerState, sheet: SheetState) {
    st.rev   // 读一下变更计数：方案改了就重组
    val ui = LocalUi.current
    var name by remember { mutableStateOf(st.plan.name) }
    var confirmDelete by remember { mutableStateOf(false) }
    T("${st.map} 的方案", 1f, ui.accent, true)
    for (p in st.site.plans) {
        Chip("${p.name}　（场上 ${p.board.size} 人）", p === st.plan, Modifier.fillMaxWidth()) { st.selectPlan(p); sheet.hide() }
    }
    androidx.compose.foundation.layout.FlowRow(Modifier.padding(top = 4.dp)) {
        Chip("＋ 新建空白方案") { st.newPlan(false); sheet.hide() }
        Chip("复制当前方案") { st.newPlan(true); sheet.hide() }
    }
    Section("改名")
    Row(verticalAlignment = Alignment.CenterVertically) {
        InputField(name, { name = it }, "方案名称", Modifier.weight(1f))
        Spacer(Modifier.width(6.dp))
        Chip("保存") { st.rename(name); sheet.hide() }
    }
    if (!confirmDelete) Chip("删除当前方案", color = Pal.Warn) { confirmDelete = true }
    else Row(verticalAlignment = Alignment.CenterVertically) {
        T("确定删除「${st.plan.name}」？", 0.9f, Pal.Warn, modifier = Modifier.padding(end = 6.dp))
        Chip("删除", color = Pal.Warn) { st.deletePlan(); sheet.hide() }
        Chip("算了") { confirmDelete = false }
    }
}
