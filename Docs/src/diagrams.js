// Генерация схем-набросков для GDD: SVG -> PNG (resvg)
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');

const OUT = path.join(__dirname, 'img');
fs.mkdirSync(OUT, { recursive: true });

const C = {
  core: { s: '#6D5BD0', f: '#EEEBFB' },
  market: { s: '#2F80ED', f: '#E6F0FD' },
  supply: { s: '#E58A2E', f: '#FDF0E3' },
  prod: { s: '#27AE60', f: '#E5F6EC' },
  hr: { s: '#D9485F', f: '#FBE8EB' },
  fin: { s: '#0E9AA7', f: '#E2F5F6' },
  dig: { s: '#B8901A', f: '#FBF5DD' },
  qc: { s: '#5E6B80', f: '#ECEFF4' },
  neu: { s: '#9AA4B2', f: '#F3F5F8' },
  ink: '#1F2A37',
  muted: '#5B6675',
  plus: '#1E8E4E',
  minus: '#C0392B',
};

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

function svg(w, h, body) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" font-family="Segoe UI">
<defs>
<marker id="a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="${C.muted}"/></marker>
<marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="${C.minus}"/></marker>
<pattern id="grid" width="20" height="20" patternUnits="userSpaceOnUse"><path d="M20 0 L0 0 0 20" fill="none" stroke="#E6E9EE" stroke-width="1"/></pattern>
</defs>
<rect width="100%" height="100%" fill="#FFFFFF"/>
${body}
</svg>`;
}

// Прямоугольник с центрированным многострочным текстом; первая строка может быть жирной
function box(x, y, w, h, lines, o = {}) {
  const pal = o.pal || C.neu;
  const fill = o.fill || pal.f;
  const stroke = o.stroke || pal.s;
  const size = o.size || 14;
  const L = Array.isArray(lines) ? lines : [lines];
  const lh = size * 1.3;
  const y0 = y + h / 2 - ((L.length - 1) * lh) / 2 + size * 0.36;
  const bold = o.bold !== false;
  const t = L.map((s, i) => {
    const sz = i === 0 ? size : (o.sub || size - 2);
    const fw = i === 0 && bold ? 700 : 400;
    const col = i === 0 ? (o.color || C.ink) : C.muted;
    return `<text x="${x + w / 2}" y="${y0 + i * lh}" text-anchor="middle" font-size="${sz}" font-weight="${fw}" fill="${col}">${esc(s)}</text>`;
  }).join('');
  const dash = o.dash ? `stroke-dasharray="${o.dash}"` : '';
  return `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${o.rx ?? 8}" fill="${fill}" stroke="${stroke}" stroke-width="${o.sw || 1.6}" ${dash}/>${t}`;
}

function line(x1, y1, x2, y2, o = {}) {
  const dash = o.dash ? `stroke-dasharray="${o.dash}"` : '';
  const ms = o.both ? 'marker-start="url(#a)"' : '';
  const me = o.noHead ? '' : `marker-end="url(#${o.red ? 'ar' : 'a'})"`;
  return `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="${o.color || C.muted}" stroke-width="${o.sw || 1.6}" ${dash} ${ms} ${me}/>`;
}

function poly(points, o = {}) {
  const dash = o.dash ? `stroke-dasharray="${o.dash}"` : '';
  const me = o.noHead ? '' : 'marker-end="url(#a)"';
  const d = points.map((p, i) => `${i ? 'L' : 'M'}${p[0]},${p[1]}`).join(' ');
  return `<path d="${d}" fill="none" stroke="${o.color || C.muted}" stroke-width="${o.sw || 1.6}" ${dash} ${me}/>`;
}

function text(x, y, s, o = {}) {
  return `<text x="${x}" y="${y}" text-anchor="${o.anchor || 'middle'}" font-size="${o.size || 12}" font-weight="${o.weight || 400}" fill="${o.color || C.muted}" ${o.italic ? 'font-style="italic"' : ''}>${esc(s)}</text>`;
}

function sign(x, y, plus) {
  return text(x, y, plus ? '+' : '−', { size: 16, weight: 700, color: plus ? C.plus : C.minus });
}

const diagrams = {};

// 1. Архитектура
diagrams.architecture = () => {
  const W = 960, H = 620;
  let b = '';
  b += `<rect x="20" y="15" width="920" height="115" rx="10" fill="#FAFBFC" stroke="${C.neu.s}" stroke-dasharray="5 4"/>`;
  b += text(36, 38, 'ПРЕДСТАВЛЕНИЕ · Unity 6, URP 2D', { anchor: 'start', size: 13, weight: 700, color: C.muted });
  const top = ['Вид цеха (сверху 3/4)', 'Экраны и панели', 'Отчёты и аналитика', 'Локализация uz / ru / en'];
  top.forEach((s, i) => { b += box(40 + i * 225, 55, 205, 56, s, { pal: C.market, size: 14 }); });
  b += line(470, 158, 470, 133); b += line(490, 133, 490, 158);
  b += text(492, 150, 'команды игрока ↑   события и состояние ↓', { anchor: 'start', size: 12 });

  b += `<rect x="20" y="160" width="920" height="265" rx="10" fill="#FBFAFF" stroke="${C.core.s}" stroke-dasharray="5 4"/>`;
  b += text(36, 183, 'ЯДРО СИМУЛЯЦИИ · C#, не зависит от графики', { anchor: 'start', size: 13, weight: 700, color: C.core.s });
  b += box(40, 195, 880, 36, 'Планировщик тиков: 1 тик = 1 игровая минута → смена → день → месяц', { pal: C.core, size: 13 });
  const r1 = [['ИИ-ядро', 'макроэкономика', C.core], ['Рынок и сбыт', 'B2C · B2B', C.market], ['Маркетинг', 'и репутация', C.market], ['Снабжение', 'биржа · поставщики', C.supply], ['Склад', 'стеллажи · ячейки', C.supply]];
  r1.forEach((m, i) => { b += box(40 + i * 178, 243, 166, 52, [m[0], m[1]], { pal: m[2], size: 14, sub: 11 }); });
  const r2 = [['Производство', 'зоны · станки · маршруты', C.prod], ['Персонал', '7 профессий · смены', C.hr], ['Финансы', 'кредит · налоги', C.fin], ['Цифровизация', '7 модулей', C.dig]];
  r2.forEach((m, i) => { b += box(129 + i * 178, 305, 166, 52, [m[0], m[1]], { pal: m[2], size: 14, sub: 11 }); });
  b += text(40, 392, 'Компаниями управляют контроллеры:', { anchor: 'start', size: 13, weight: 700, color: C.ink });
  b += box(330, 372, 170, 38, 'Игрок', { pal: C.neu, size: 13 });
  b += box(515, 372, 190, 38, 'Бот · ML-Agents', { pal: C.core, size: 13 });
  b += box(720, 372, 200, 38, 'Удалённый игрок (позже)', { pal: C.neu, size: 13, dash: '5 4', bold: false });

  b += box(20, 455, 290, 70, ['Данные', 'ScriptableObject / JSON: продукты,', 'станки, сырьё, профессии, баланс'], { pal: C.neu, size: 14, sub: 11 });
  b += box(335, 455, 290, 70, ['ML-Agents', 'политики ботов в ONNX,', 'запуск через Inference Engine'], { pal: C.core, size: 14, sub: 11 });
  b += box(650, 455, 290, 70, ['LLM-«голос»', 'llama.cpp + модель GGUF с HF,', 'готовый текст → в интерфейс'], { pal: C.market, size: 14, sub: 11 });
  b += box(20, 545, 605, 56, ['Лаборатория сценариев', 'то же ядро без графики → прогоны → Excel / CSV'], { pal: C.fin, size: 14, sub: 11 });
  b += box(650, 545, 290, 56, ['Сохранения', 'автосейв в конце месяца + 3 слота'], { pal: C.neu, size: 14, sub: 11 });

  b += line(165, 455, 165, 427);
  b += line(480, 455, 590, 412);
  b += line(795, 455, 795, 427);
  b += text(806, 446, 'факты', { anchor: 'start', size: 11 });
  b += poly([[940, 470], [952, 470], [952, 100], [921, 100]], {});
  return { svg: svg(W, H, b), W, H };
};

// 2. Игровой цикл и время
diagrams.timeloop = () => {
  const W = 960, H = 300;
  let b = '';
  const items = [
    ['Тик · 1 игр. минута', 'движение рабочих', 'операции станков', 'износ, усталость', C.prod],
    ['Смена · 8 часов', 'итоги смены', 'восстановление сил', 'ТО в окне смены', C.hr],
    ['День', 'поставки сырья', 'расходы склада', 'штрафы, события', C.supply],
    ['Месяц · 28–31 день', 'финансы, налоги, кредит', 'тендеры, ИИ-ядро', 'решения ботов', C.core],
    ['Пауза + отчёт', 'сводка (LLM)', 'решения игрока', '→ следующий месяц', C.market],
  ];
  items.forEach((it, i) => {
    const x = 20 + i * 188;
    b += box(x, 50, 168, 120, [it[0], it[1], it[2], it[3]], { pal: it[4], size: 14, sub: 12 });
    if (i < 4) b += line(x + 168, 110, x + 188, 110);
  });
  b += poly([[866, 170], [866, 212], [104, 212], [104, 172]], { dash: '6 4' });
  b += text(485, 230, 'новый месяц: игрок меняет цены, закупки, смены, найм — и запускает время', { size: 12 });
  b += text(20, 30, 'Вложенные масштабы времени', { anchor: 'start', size: 13, weight: 700, color: C.ink });
  b += text(20, 270, 'Скорости: пауза · x1 · x2 · x4 · x8.  На x1 смена ≈ 40 с, месяц ≈ 15 мин.  Нерабочие часы проматываются быстро.', { anchor: 'start', size: 12, color: C.ink });
  b += text(20, 290, 'Лаборатория сценариев гоняет те же тики без графики — с максимальной скоростью.', { anchor: 'start', size: 12 });
  return { svg: svg(W, H, b), W, H };
};

// 3. Поток материалов и продукции
diagrams.flow = () => {
  const W = 960, H = 410;
  let b = '';
  b += box(20, 70, 110, 56, 'Поставщики', { pal: C.supply, size: 13 });
  b += box(150, 70, 100, 56, 'Разгрузка', { pal: C.supply, size: 13 });
  b += box(270, 70, 115, 56, ['Входной', 'контроль'], { pal: C.qc, size: 13, sub: 13 });
  b += box(405, 70, 110, 56, ['Склад', 'сырья'], { pal: C.supply, size: 13, sub: 13 });
  b += box(555, 25, 105, 56, 'Раскрой', { pal: C.prod, size: 13 });
  b += box(555, 115, 105, 56, 'Формовка', { pal: C.prod, size: 13 });
  b += box(690, 70, 95, 56, 'Пошив', { pal: C.prod, size: 13 });
  b += box(810, 70, 130, 56, ['Выходной', 'контроль'], { pal: C.qc, size: 13, sub: 13 });
  b += box(810, 215, 130, 56, ['Склад готовой', 'продукции'], { pal: C.supply, size: 13, sub: 13 });
  b += box(600, 190, 170, 46, 'B2C · розница', { pal: C.market, size: 13 });
  b += box(600, 255, 170, 46, 'B2B · тендеры', { pal: C.market, size: 13 });
  b += box(400, 222, 140, 46, 'Покупатели', { pal: C.neu, size: 13 });
  b += box(200, 222, 140, 46, 'Репутация', { pal: C.core, size: 13 });
  b += box(470, 322, 470, 44, 'Брак: хранить · утилизировать · ремонт (→ пошив) · уценка', { pal: C.hr, size: 13 });

  b += line(130, 98, 150, 98);
  b += line(250, 98, 270, 98);
  b += line(385, 98, 405, 98);
  b += line(515, 90, 555, 55);
  b += line(515, 106, 555, 141);
  b += line(600, 82, 600, 114); b += line(615, 114, 615, 82);
  b += line(660, 55, 690, 88);
  b += line(660, 141, 690, 108);
  b += line(785, 98, 810, 98);
  b += line(875, 126, 875, 215);
  b += line(810, 235, 771, 214);
  b += line(810, 251, 771, 277);
  b += line(600, 213, 541, 240);
  b += line(600, 278, 541, 252);
  b += line(400, 245, 341, 245, { dash: '5 4', red: true, color: C.minus });
  b += text(370, 214, 'рекламации', { size: 11, color: C.minus });
  b += poly([[940, 98], [952, 98], [952, 344], [941, 344]], {});
  b += poly([[327, 126], [327, 160], [75, 160], [75, 127]], { dash: '5 4' });
  b += text(200, 178, 'брак в партии → замена поставщиком', { size: 11 });
  b += text(460, 146, 'перегруз → штраф', { size: 11 });
  b += text(20, 398, 'Рецепты задают путь: добок — раскрой → пошив; шлем и защита — формовка → пошив; хогу — раскрой → формовка → пошив', { anchor: 'start', size: 12, color: C.ink });
  b += text(20, 380, 'Все перемещения между зонами выполняют разнорабочие — партиями заданного размера и по разрешённым проходам.', { anchor: 'start', size: 12 });
  return { svg: svg(W, H, b), W, H };
};

// 4. Набросок планировки цеха (вид сверху)
diagrams.layout = () => {
  const W = 960, H = 620;
  let b = '';
  b += `<rect x="20" y="20" width="920" height="520" fill="url(#grid)" stroke="${C.ink}" stroke-width="2.5"/>`;
  // ворота
  b += `<rect x="14" y="250" width="12" height="70" fill="#FFFFFF"/>`;
  b += text(30, 245, 'ворота', { anchor: 'start', size: 11, color: C.ink });
  const zone = (x, y, w, h, label, pal) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${pal.f}" fill-opacity="0.75" stroke="${pal.s}" stroke-width="1.5"/>` + text(x + 8, y + 18, label, { anchor: 'start', size: 13, weight: 700, color: pal.s });
  b += zone(20, 20, 140, 520, 'Разгрузка / отгрузка', C.supply);
  b += zone(160, 20, 170, 230, 'Склад сырья', C.supply);
  b += zone(330, 20, 230, 230, 'Раскрой', C.prod);
  b += zone(560, 20, 200, 230, 'Формовка', C.prod);
  b += zone(760, 20, 180, 520, 'Пошив', C.prod);
  b += zone(160, 310, 170, 230, 'Склад ГП', C.supply);
  b += zone(330, 310, 140, 230, 'Офис', C.fin);
  b += zone(470, 310, 290, 230, 'ОТК', C.qc);
  // главный проход
  b += `<rect x="160" y="250" width="600" height="60" fill="#FFFFFF" fill-opacity="0.9" stroke="none"/>`;
  b += text(460, 285, 'главный проход', { size: 11, italic: true });
  // стеллажи
  for (let i = 0; i < 4; i++) b += `<rect x="${178 + i * 38}" y="45" width="22" height="150" fill="#FFFFFF" stroke="${C.supply.s}"/>`;
  for (let i = 0; i < 3; i++) b += `<rect x="${178 + i * 48}" y="335" width="26" height="150" fill="#FFFFFF" stroke="${C.supply.s}"/>`;
  // раскройные столы
  for (let i = 0; i < 3; i++) b += `<rect x="350" y="${50 + i * 62}" width="120" height="34" rx="3" fill="#FFFFFF" stroke="${C.prod.s}" stroke-width="1.5"/>`;
  // прессы
  for (let i = 0; i < 3; i++) b += `<rect x="${585 + i * 58}" y="70" width="40" height="40" rx="3" fill="#FFFFFF" stroke="${C.prod.s}" stroke-width="1.5"/>`;
  b += `<rect x="643" y="150" width="40" height="40" rx="3" fill="#FFFFFF" stroke="${C.prod.s}" stroke-width="1.5"/>`;
  // узкое место
  b += `<circle cx="701" cy="68" r="11" fill="${C.minus}"/>` + text(701, 73, '!', { size: 14, weight: 700, color: '#FFFFFF' });
  b += text(705, 130, 'узкое место:', { size: 11, color: C.minus });
  b += text(705, 144, 'загрузка 98%', { size: 11, color: C.minus });
  // швейные машины
  for (let r = 0; r < 6; r++) for (let c = 0; c < 2; c++) b += `<rect x="${785 + c * 75}" y="${50 + r * 72}" width="50" height="30" rx="3" fill="#FFFFFF" stroke="${C.prod.s}" stroke-width="1.5"/>`;
  // подача: фронтальная и сзади
  b += `<path d="M797,80 l8,10 l8,-10 z" fill="${C.supply.s}"/>`;
  b += `<path d="M872,50 l8,-10 l8,10 z" fill="${C.minus}"/>`;
  
  // ОТК столы
  b += `<rect x="500" y="350" width="90" height="36" rx="3" fill="#FFFFFF" stroke="${C.qc.s}" stroke-width="1.5"/>`;
  b += `<rect x="620" y="350" width="90" height="36" rx="3" fill="#FFFFFF" stroke="${C.qc.s}" stroke-width="1.5"/>`;
  b += text(545, 405, 'выходной', { size: 11 });
  b += text(665, 405, 'выходной', { size: 11 });
  b += `<rect x="45" y="390" width="90" height="36" rx="3" fill="#FFFFFF" stroke="${C.qc.s}" stroke-width="1.5"/>`;
  b += text(90, 444, 'вх. контроль', { size: 11 });
  // офис
  b += `<rect x="350" y="350" width="44" height="28" rx="3" fill="#FFFFFF" stroke="${C.fin.s}"/>`;
  b += `<rect x="410" y="350" width="44" height="28" rx="3" fill="#FFFFFF" stroke="${C.fin.s}"/>`;
  // грузовик
  b += `<rect x="40" y="100" width="70" height="36" rx="4" fill="#FFFFFF" stroke="${C.supply.s}" stroke-width="1.5"/>` + text(75, 123, 'грузовик', { size: 11 });
  // маршруты (диаграмма спагетти)
  const route = (pts) => poly(pts, { dash: '6 4', color: C.supply.s, sw: 2, noHead: true });
  b += route([[60, 285], [190, 280], [245, 230]]);
  b += route([[245, 265], [410, 270], [410, 230]]);
  b += route([[470, 290], [640, 290], [640, 240]]);
  b += route([[680, 280], [790, 280]]);
  b += route([[760, 300], [600, 300], [600, 345]]);
  b += route([[480, 300], [245, 300], [245, 330]]);
  b += route([[160, 420], [90, 330]]);
  const cnt = (x, y, n) => `<circle cx="${x}" cy="${y}" r="13" fill="#FFFFFF" stroke="${C.supply.s}" stroke-width="1.5"/>` + text(x, y + 4, n, { size: 11, weight: 700, color: C.ink });
  b += cnt(150, 282, '42'); b += cnt(330, 270, '36'); b += cnt(560, 290, '28'); b += cnt(735, 280, '30'); b += cnt(690, 300, '40'); b += cnt(360, 300, '38'); b += cnt(120, 380, '35');
  // рабочие
  const P = { lab: C.supply.s, cut: C.prod.s, sew: '#E2556B', mold: '#2E7D32', mech: C.core.s, qc: C.fin.s, mgr: C.market.s };
  const dot = (x, y, col) => `<circle cx="${x}" cy="${y}" r="7" fill="${col}" stroke="#FFFFFF" stroke-width="1.5"/>`;
  b += dot(205, 280, P.lab); b += dot(500, 290, P.lab); b += dot(620, 300, P.lab);
  b += dot(410, 98, P.cut); b += dot(410, 160, P.cut);
  b += dot(605, 122, P.mold); b += dot(663, 122, P.mold);
  for (let r = 0; r < 5; r++) b += dot(810, 95 + r * 72, P.sew);
  b += dot(880, 240, P.mech);
  b += dot(545, 395 - 25, P.qc); b += dot(90, 410 + 20, P.qc);
  b += dot(372, 392, P.mgr);
  // легенда
  const leg = [['разнорабочий', P.lab], ['раскройщик', P.cut], ['швея', P.sew], ['формовщик', P.mold], ['наладчик', P.mech], ['контролёр ОТК', P.qc], ['менеджер', P.mgr]];
  let lx = 20;
  leg.forEach(([n, col]) => { b += dot(lx + 7, 566, col) + text(lx + 20, 570, n, { anchor: 'start', size: 12, color: C.ink }); lx += 30 + n.length * 7.2; });
  b += `<line x1="20" y1="600" x2="60" y2="600" stroke="${C.supply.s}" stroke-width="2" stroke-dasharray="6 4"/>` + text(68, 604, 'маршрут разнорабочих', { anchor: 'start', size: 12, color: C.ink });
  b += cnt(260, 600, '42') + text(280, 604, 'проходов за неделю (диаграмма спагетти)', { anchor: 'start', size: 12, color: C.ink });
  b += '<path d="M600,604 l7,-10 l7,10 z" fill="' + C.supply.s + '"/>' + text(620, 604, 'фронтальная подача', { anchor: 'start', size: 12, color: C.ink });
  b += '<path d="M770,604 l7,-10 l7,10 z" fill="' + C.minus + '"/>' + text(790, 604, 'подача сзади', { anchor: 'start', size: 12, color: C.ink });
  return { svg: svg(W, H, b), W, H };
};

// 5. Причинно-следственная карта экономики
diagrams.causal = () => {
  const W = 980, H = 505;
  let b = '';
  const inp = ['Качество сырья', 'Навык рабочих', 'Состояние станков', 'Усталость'];
  inp.forEach((s, i) => { b += box(20, 20 + i * 52, 150, 40, s, { pal: C.prod, size: 13 }); });
  b += box(230, 40, 165, 44, 'Качество продукта', { pal: C.prod, size: 13 });
  b += box(230, 140, 165, 44, 'Вероятность брака', { pal: C.hr, size: 13 });
  b += box(450, 140, 150, 44, 'Рекламации', { pal: C.hr, size: 13 });
  b += box(660, 140, 150, 44, 'Репутация', { pal: C.core, size: 13 });
  b += box(660, 30, 150, 50, ['Маркетинг,', 'сертификация'], { pal: C.market, size: 13, sub: 13 });
  // связи верхнего яруса
  b += line(170, 40, 229, 56);
  b += line(170, 92, 229, 64);
  b += line(170, 144, 229, 76);
  b += line(170, 46, 229, 150);
  b += line(170, 98, 229, 156);
  b += line(170, 150, 229, 162);
  b += line(170, 196, 229, 172);
  b += line(395, 162, 449, 162); b += sign(406, 154, true);
  b += line(600, 162, 659, 162); b += sign(630, 154, false);
  b += line(735, 80, 735, 139); b += sign(748, 115, true);
  // нижний ярус
  b += box(20, 300, 150, 44, 'Цена', { pal: C.market, size: 13 });
  b += box(230, 300, 165, 44, 'Привлекательность', { pal: C.market, size: 13 });
  b += box(450, 300, 150, 44, 'Доля рынка', { pal: C.market, size: 13 });
  b += box(450, 400, 150, 50, ['Привлекательность', 'конкурентов'], { pal: C.neu, size: 12, sub: 12 });
  b += box(660, 300, 150, 50, ['Продажи', '≤ запас на складе'], { pal: C.market, size: 13, sub: 11 });
  b += box(660, 400, 150, 50, ['Спрос рынка', 'экономика · сезон · цены'], { pal: C.core, size: 13, sub: 11 });
  b += box(845, 300, 125, 44, 'Прибыль', { pal: C.fin, size: 13 });
  b += box(845, 400, 125, 50, ['Затраты', 'сырьё · ЗП · ТО'], { pal: C.fin, size: 13, sub: 11 });
  b += box(845, 195, 125, 44, 'Капитализация', { pal: C.fin, size: 13 });
  b += line(170, 322, 229, 322); b += sign(200, 314, false);
  b += poly([[395, 62], [422, 62], [422, 270], [370, 270], [370, 299]]); b += sign(382, 290, true);
  b += poly([[735, 184], [735, 250], [300, 250], [300, 299]]); b += sign(288, 290, true);
  b += line(395, 322, 449, 322); b += sign(425, 314, true);
  b += line(525, 400, 525, 345); b += sign(538, 380, false);
  b += line(600, 322, 659, 322); b += sign(630, 314, true);
  b += line(735, 400, 735, 351); b += sign(748, 382, true);
  b += line(810, 322, 844, 322);
  b += line(907, 400, 907, 345); b += sign(920, 382, false);
  b += line(907, 300, 907, 240); b += sign(920, 272, true);
  b += line(810, 162, 870, 194); b += sign(845, 166, true);
  b += text(20, 477, 'Качество сырья, навык и состояние станков повышают качество продукта и снижают вероятность брака; усталость повышает брак.', { anchor: 'start', size: 12, color: C.ink });
  b += text(20, 496, 'Зелёный «+» — рост причины увеличивает следствие, красный «−» — уменьшает.', { anchor: 'start', size: 12 });
  return { svg: svg(W, H, b), W, H };
};

// 6. Боты: обучение и месячный цикл
diagrams.bots = () => {
  const W = 960, H = 340;
  let b = '';
  b += text(20, 20, 'Конвейер обучения', { anchor: 'start', size: 13, weight: 700, color: C.ink });
  const st = [['Прогоны', 'полного цеха', C.prod], ['Калибровка', 'суррогатной модели', C.core], ['Обучение PPO', 'self-play, 2 архетипа', C.core], ['Дообучение', 'на полном цехе', C.prod], ['Экспорт ONNX', '→ в игру (Inference Engine)', C.market]];
  st.forEach((s, i) => {
    const x = 20 + i * 188;
    b += box(x, 32, 168, 66, [s[0], s[1]], { pal: s[2], size: 14, sub: 11 });
    if (i < 4) b += line(x + 168, 65, x + 188, 65);
  });
  b += box(560, 118, 300, 34, 'Hugging Face Hub: хранение и версии моделей', { pal: C.neu, size: 12, dash: '5 4', bold: false });
  b += line(852, 98, 820, 117, { dash: '4 3' });
  b += text(20, 190, 'Цикл решения бота (раз в игровой месяц)', { anchor: 'start', size: 13, weight: 700, color: C.ink });
  b += box(20, 205, 260, 64, ['Наблюдения', 'финансы, запасы, мощности, брак,', 'рынок, публичные данные конкурента'], { pal: C.neu, size: 13, sub: 11 });
  b += box(310, 205, 150, 64, ['Политика', 'нейросеть бота'], { pal: C.core, size: 13, sub: 11 });
  b += box(490, 205, 280, 64, ['Действия', 'цены, объёмы, сырьё, найм, станки,', 'бюджеты маркетинга и ТО, заявки'], { pal: C.market, size: 13, sub: 11 });
  b += box(800, 205, 140, 64, ['Месяц', 'симуляции'], { pal: C.prod, size: 13, sub: 13 });
  b += line(280, 237, 309, 237); b += line(460, 237, 489, 237); b += line(770, 237, 799, 237);
  b += poly([[870, 269], [870, 300], [150, 300], [150, 271]], { dash: '6 4' });
  b += text(510, 320, 'награда: изменение капитализации за месяц, большой штраф за банкротство', { size: 12 });
  return { svg: svg(W, H, b), W, H };
};

// 7. LLM-конвейер
diagrams.llm = () => {
  const W = 960, H = 220;
  let b = '';
  const st = [['Факты симуляции', 'события, решения, KPI', C.core], ['Шаблон промпта', 'язык · роль · формат', C.neu], ['Локальная LLM', 'GGUF · llama.cpp', C.market], ['Проверка', 'числа = факты', C.hr], ['Текст в игре', 'новости · отчёт', C.prod]];
  st.forEach((s, i) => {
    const x = 20 + i * 186;
    b += box(x, 30, 166, 70, [s[0], s[1]], { pal: s[2], size: 14, sub: 12 });
    if (i < 4) b += line(x + 166, 65, x + 186, 65);
  });
  b += poly([[661, 100], [661, 140], [289, 140], [289, 102]], { dash: '6 4' });
  b += text(475, 158, 'не прошло проверку → повторная генерация или готовый шаблон', { size: 12 });
  b += text(20, 195, 'LLM никогда не придумывает числа: все значения подставляются из симуляции.', { anchor: 'start', size: 12, color: C.ink });
  b += text(20, 213, 'Узбекский: если качество модели слабое — шаблонные тексты с подстановкой чисел.', { anchor: 'start', size: 12 });
  return { svg: svg(W, H, b), W, H };
};

// 8. Ветка цифровизации
diagrams.digital = () => {
  const W = 960, H = 330;
  let b = '';
  b += box(20, 128, 150, 54, ['Цифровизация', 'ветка технологий'], { pal: C.dig, size: 14, sub: 11 });
  const mods = [
    ['1. IoT-датчики', 'точное состояние каждого станка'],
    ['2. Предиктивное ТО (нужны датчики)', 'меньше поломок и внеплановых ремонтов'],
    ['3. Цифровой двойник цеха', 'проверка перестановки «что если» до оплаты'],
    ['4. ИИ-раскрой', 'меньше отходов ткани'],
    ['5. ИИ-контроль качества', 'брак ловится до отгрузки'],
    ['6. Прогноз спроса', 'рекомендации по закупкам и запасам'],
    ['7. ИИ-ценообразование', 'автоматические цены по каналам'],
  ];
  b += `<line x1="170" y1="155" x2="200" y2="155" stroke="${C.muted}" stroke-width="1.6"/>`;
  b += `<line x1="200" y1="31" x2="200" y2="271" stroke="${C.muted}" stroke-width="1.6"/>`;
  mods.forEach((m, i) => {
    const y = 15 + i * 40;
    b += line(200, y + 16, 229, y + 16);
    b += box(230, y, 290, 32, m[0], { pal: C.dig, size: 13, rx: 6 });
    b += line(520, y + 16, 549, y + 16);
    b += box(550, y, 390, 32, m[1], { pal: C.neu, size: 13, rx: 6, bold: false });
  });
  
  b += text(20, 318, 'Каждый модуль: вложения + ежемесячная лицензия. Эффект измеряется в Лаборатории сценариев.', { anchor: 'start', size: 12, color: C.ink });
  return { svg: svg(W, H, b), W, H };
};

// 9. Лаборатория сценариев
diagrams.lab = () => {
  const W = 960, H = 300;
  let b = '';
  b += box(20, 105, 170, 70, ['Базовая фабрика', 'параметры Узбекистана'], { pal: C.neu, size: 14, sub: 11 });
  const sc = [['A · без цифровизации', C.neu, null], ['B · + датчики и предиктивное ТО', C.dig, null], ['C · + ИИ-раскрой', C.dig, null], ['D · + ИИ-контроль качества', C.dig, null], ['… любые комбинации модулей', C.neu, '5 4']];
  sc.forEach((s, i) => {
    const y = 25 + i * 44;
    b += box(250, y, 250, 34, s[0], { pal: s[1], size: 13, rx: 6, dash: s[2], bold: i === 0 });
    b += line(190, 140, 249, y + 17);
    b += line(500, y + 17, 549, 140);
  });
  b += text(375, 252, 'каждый сценарий × N прогонов с разными seed', { size: 12 });
  b += box(550, 105, 140, 70, ['Прогон', 'без графики'], { pal: C.core, size: 14, sub: 13 });
  b += box(730, 50, 210, 64, ['Excel / CSV', 'KPI каждого прогона'], { pal: C.fin, size: 14, sub: 11 });
  b += box(730, 160, 210, 64, ['Главы ВКР 3.2 и 3.3', 'эффект · ROI · окупаемость'], { pal: C.market, size: 14, sub: 11 });
  b += line(690, 130, 729, 90); b += line(835, 114, 835, 159);
  b += text(20, 290, 'Эффект модуля = разница средних KPI между сценарием с модулем и базовым сценарием A.', { anchor: 'start', size: 12, color: C.ink });
  return { svg: svg(W, H, b), W, H };
};

// 10. Макет главного экрана
diagrams.ui = () => {
  const W = 960, H = 600;
  let b = '';
  b += `<rect x="20" y="20" width="920" height="560" rx="10" fill="#FFFFFF" stroke="${C.ink}" stroke-width="2"/>`;
  b += `<rect x="20" y="20" width="920" height="50" rx="10" fill="#F3F5F8"/>`;
  b += `<rect x="20" y="50" width="920" height="20" fill="#F3F5F8"/>`;
  b += text(36, 52, 'GEMINIUM', { anchor: 'start', size: 18, weight: 700, color: C.core.s });
  b += text(160, 50, 'Год 1 · Месяц 3 · День 12 · 10:40', { anchor: 'start', size: 13, color: C.ink });
  b += box(400, 30, 170, 30, 'Баланс: 1 240 млн сум', { pal: C.fin, size: 12, rx: 15 });
  b += box(580, 30, 110, 30, 'Репутация: 62', { pal: C.core, size: 12, rx: 15 });
  ['||', 'x1', 'x2', 'x4', 'x8'].forEach((s, i) => { b += box(705 + i * 32, 30, 28, 30, s, { pal: i === 2 ? C.market : C.neu, size: 12, rx: 5 }); });
  b += box(870, 30, 56, 30, 'RU', { pal: C.neu, size: 12, rx: 5, bold: false });
  const nav = ['Цех', 'Склад', 'Снабжение', 'Персонал', 'Сбыт', 'Маркетинг', 'Финансы', 'Цифровизация', 'Аналитика', 'Задание'];
  b += `<rect x="20" y="70" width="150" height="510" fill="#FAFBFC" stroke="#E1E5EB"/>`;
  nav.forEach((s, i) => {
    if (i === 0) b += `<rect x="28" y="${80 + i * 46}" width="134" height="36" rx="6" fill="${C.market.f}" stroke="${C.market.s}"/>`;
    b += text(44, 103 + i * 46, s, { anchor: 'start', size: 13, color: i === 0 ? C.market.s : C.ink, weight: i === 0 ? 700 : 400 });
  });
  // вид цеха
  b += `<rect x="170" y="70" width="530" height="450" fill="url(#grid)" stroke="#E1E5EB"/>`;
  const z = (x, y, w, h, l, pal) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${pal.f}" stroke="${pal.s}"/>` + text(x + 6, y + 15, l, { anchor: 'start', size: 11, weight: 700, color: pal.s });
  b += z(185, 85, 120, 190, 'Склад сырья', C.supply);
  b += z(305, 85, 150, 190, 'Раскрой', C.prod);
  b += z(455, 85, 120, 190, 'Формовка', C.prod);
  b += z(575, 85, 110, 420, 'Пошив', C.prod);
  b += z(185, 320, 120, 185, 'Склад ГП', C.supply);
  b += z(305, 320, 270, 185, 'ОТК', C.qc);
  b += `<rect x="478" y="130" width="34" height="34" rx="3" fill="#FFFFFF" stroke="${C.minus}" stroke-width="3"/>`;
  b += `<rect x="525" y="130" width="34" height="34" rx="3" fill="#FFFFFF" stroke="${C.prod.s}"/>`;
  b += `<rect x="478" y="200" width="34" height="34" rx="3" fill="#FFFFFF" stroke="${C.prod.s}"/>`;
  for (let r = 0; r < 5; r++) b += `<rect x="595" y="${115 + r * 76}" width="70" height="28" rx="3" fill="#FFFFFF" stroke="${C.prod.s}"/>`;
  for (let i = 0; i < 3; i++) b += `<rect x="320" y="${110 + i * 55}" width="110" height="28" rx="3" fill="#FFFFFF" stroke="${C.prod.s}"/>`;
  b += poly([[300, 300], [470, 300], [495, 236]], { dash: '6 4', color: C.supply.s, sw: 2, noHead: true });
  // правая панель
  b += `<rect x="700" y="70" width="240" height="450" fill="#FFFFFF" stroke="#E1E5EB"/>`;
  b += text(716, 98, 'Термопресс #2', { anchor: 'start', size: 15, weight: 700, color: C.ink });
  const rows = [['Состояние', '46% ▼', C.minus], ['Загрузка', '98% — узкое место', C.minus], ['Брак за неделю', '7,2%', C.ink], ['Оператор', 'Dilnoza, опыт 54', C.ink], ['Усталость', '63%', C.ink], ['Регламент ТО', 'каждые 400 операций', C.ink]];
  rows.forEach((r, i) => {
    b += text(716, 130 + i * 40, r[0], { anchor: 'start', size: 11 });
    b += text(716, 146 + i * 40, r[1], { anchor: 'start', size: 13, weight: 700, color: r[2] });
  });
  b += box(716, 382, 208, 32, 'Ремонт сейчас', { pal: C.hr, size: 12, rx: 6 });
  b += box(716, 422, 208, 32, 'Улучшить станок', { pal: C.neu, size: 12, rx: 6 });
  b += box(716, 462, 208, 32, 'Переместить', { pal: C.neu, size: 12, rx: 6 });
  // лента новостей
  b += `<rect x="170" y="520" width="770" height="60" fill="${C.core.f}" stroke="#E1E5EB"/>`;
  b += box(184, 535, 48, 28, 'LLM', { pal: C.core, size: 11, rx: 5 });
  b += text(244, 546, 'Перед чемпионатом Азии спрос на шлемы вырос на 18%.', { anchor: 'start', size: 13, color: C.ink });
  b += text(244, 566, 'Daebo поднял цены на хогу на 5%: у них загружены все прессы.', { anchor: 'start', size: 13, color: C.muted });
  return { svg: svg(W, H, b), W, H };
};

// 11. Дорожная карта
diagrams.roadmap = () => {
  const months = ['Ноя', 'Дек', 'Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн'];
  const W = 960, H = 390;
  const x0 = 230, cw = 88;
  let b = '';
  months.forEach((m, i) => {
    b += `<rect x="${x0 + i * cw}" y="20" width="${cw}" height="30" fill="${i % 2 ? '#F3F5F8' : '#E9EDF2'}"/>`;
    b += text(x0 + i * cw + cw / 2, 40, m, { size: 13, weight: 700, color: C.ink });
    b += `<line x1="${x0 + i * cw}" y1="50" x2="${x0 + i * cw}" y2="350" stroke="#EEF1F4"/>`;
  });
  b += text(x0, 14, '2026', { anchor: 'start', size: 11 });
  b += text(x0 + 2 * cw, 14, '2027', { anchor: 'start', size: 11 });
  const rows = [
    ['Ядро и экономика', 0, 2, C.core],
    ['Пространственный цех', 2, 2, C.prod],
    ['Сбыт и маркетинг', 4, 1, C.market],
    ['Боты (ML-Agents)', 4, 1.5, C.core],
    ['UI, языки, LLM, кампания', 5, 1, C.dig],
    ['Лаборатория сценариев', 5, 1, C.fin],
    ['Баланс и прогоны для ВКР', 6, 1, C.hr],
  ];
  rows.forEach((r, i) => {
    const y = 64 + i * 38;
    b += text(20, y + 18, r[0], { anchor: 'start', size: 13, color: C.ink });
    b += `<rect x="${x0 + r[1] * cw + 3}" y="${y}" width="${r[2] * cw - 6}" height="26" rx="5" fill="${r[3].f}" stroke="${r[3].s}" stroke-width="1.5"/>`;
  });
  const yd = 64 + 7 * 38;
  b += text(20, yd + 18, 'Защита', { anchor: 'start', size: 13, weight: 700, color: C.ink });
  const cx = x0 + 7 * cw + cw / 2;
  b += `<path d="M${cx},${yd} l13,13 l-13,13 l-13,-13 z" fill="${C.minus}"/>`;
  const xf = x0 + 6 * cw;
  b += `<line x1="${xf}" y1="50" x2="${xf}" y2="352" stroke="${C.minus}" stroke-width="2" stroke-dasharray="6 4"/>`;
  b += text(xf, 372, 'заморозка функций — 1 мая 2027', { size: 12, weight: 700, color: C.minus });
  return { svg: svg(W, H, b), W, H };
};

const meta = {};
for (const [name, fn] of Object.entries(diagrams)) {
  const { svg: s, W, H } = fn();
  fs.writeFileSync(path.join(OUT, name + '.svg'), s);
  const r = new Resvg(s, { fitTo: { mode: 'width', value: W * 2 }, font: { loadSystemFonts: true, defaultFontFamily: 'Segoe UI' } });
  fs.writeFileSync(path.join(OUT, name + '.png'), r.render().asPng());
  meta[name] = { W, H };
}
fs.writeFileSync(path.join(OUT, 'meta.json'), JSON.stringify(meta, null, 2));
console.log('ok', Object.keys(meta).join(', '));
