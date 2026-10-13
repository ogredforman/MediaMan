-- MediaMan on-screen controller (SPEC §7.4 step 3, §8 playback [M], §11): the title, a progress bar with chapters,
-- play/pause, skip back and forward, previous and next episode, time, audio and subtitle menus, and the "Up next"
-- countdown, in the purple theme over a dark gradient. Loaded with --script by the desktop app,
-- with the built-in OSC off (mpv.conf). It talks to the desktop main process only through MPV script messages and
-- properties (.claude/rules/desktop.md). Needs MPV 0.37 or later (the mouse-pos property).
--
-- Keys: `a` opens the audio menu and `u` the subtitle menu (rebind with `script-binding osc/audio-menu` and
-- `script-binding osc/subtitle-menu` in input.conf); in a menu, Up/Down move, Enter picks and Esc closes.
--
-- Skip back/forward seek by `skip_back`/`skip_forward` seconds (bindable as `script-binding osc/skip-back` and
-- `osc/skip-forward`). Episodes: once the file loads the main process sends
-- `script-message mediaman-episodes <previous title> <next title>` (empty for none); the buttons, and the PREV/NEXT
-- media keys (`osc/previous-episode`, `osc/next-episode`), answer `script-message mediaman-episode previous|next`.
--
-- Up next (SPEC §7.4 step 5): the main process sends `script-message mediaman-up-next <seconds> <title>`; the card
-- counts down and answers `script-message mediaman-up-next-choice play` when it runs out or on Play now (Enter), or
-- `... cancel` on Cancel (Esc) or a seek that lands before where the card appeared.

local assdraw = require 'mp.assdraw'
local options = require 'mp.options'

local opts = {
  hide_timeout = 2, -- seconds without mouse movement before the controls hide
  scale = 1, -- multiplies the size taken from the window height
  skip_back = 10, -- seconds the skip back button goes back
  skip_forward = 30, -- seconds the skip forward button goes ahead
}
options.read_options(opts, 'mediaman_osc')

-- SPEC §11 colours as ASS BGR hex; alpha 00 is opaque and FF transparent.
local color = {
  background = '140B0E', -- #0E0B14
  surface = '1F1217', -- #17121F
  accent = 'F65C8B', -- #8B5CF6
  highlight = 'FDB5C4', -- #C4B5FD
  text = 'FFFFFF',
}

local languages = {
  en = 'English', eng = 'English', fr = 'French', fre = 'French', fra = 'French', de = 'German', ger = 'German',
  deu = 'German', es = 'Spanish', spa = 'Spanish', it = 'Italian', ita = 'Italian', pt = 'Portuguese',
  por = 'Portuguese', nl = 'Dutch', dut = 'Dutch', nld = 'Dutch', sv = 'Swedish', swe = 'Swedish', da = 'Danish',
  dan = 'Danish', no = 'Norwegian', nor = 'Norwegian', fi = 'Finnish', fin = 'Finnish', pl = 'Polish',
  pol = 'Polish', ru = 'Russian', rus = 'Russian', ja = 'Japanese', jpn = 'Japanese', ko = 'Korean',
  kor = 'Korean', zh = 'Chinese', chi = 'Chinese', zho = 'Chinese', ar = 'Arabic', ara = 'Arabic',
  hi = 'Hindi', hin = 'Hindi', tr = 'Turkish', tur = 'Turkish',
}

local codecs = {
  subrip = 'SRT', ass = 'ASS', ssa = 'SSA', webvtt = 'VTT', mov_text = 'Text', hdmv_pgs_subtitle = 'PGS',
  dvd_subtitle = 'VobSub', dvb_subtitle = 'DVB', eac3 = 'E-AC3', truehd = 'TrueHD', opus = 'Opus',
  vorbis = 'Vorbis', flac = 'FLAC',
}

local channel_names = { [1] = 'Mono', [2] = 'Stereo', [6] = '5.1', [8] = '7.1' }

local state = {
  w = 0,
  h = 0,
  s = 1,
  mouse = { x = -1, y = -1, hover = false },
  show_until = 0,
  paused = false,
  position = nil,
  duration = nil,
  title = '',
  chapters = {},
  tracks = {},
  dragging = false,
  menu = nil, -- { kind = 'audio' | 'sub', items = { { label, id, selected } }, focus = n }
  up_next = nil, -- { title, seconds, deadline, timer }
  episodes = nil, -- { previous = title or nil, next = title or nil } for an episode
  elements = {}, -- clickable areas from the last render
  click_bound = false,
}

local overlay = mp.create_osd_overlay('ass-events')

-- Defined under Input; drawing and the click binding call them.
local toggle_menu, on_click, finish_up_next

-- Helpers --------------------------------------------------------------------------------------------------------

local function format_time(seconds)
  if not seconds or seconds < 0 then seconds = 0 end
  seconds = math.floor(seconds)
  local h, m, s = math.floor(seconds / 3600), math.floor(seconds / 60) % 60, seconds % 60
  if h > 0 then return string.format('%d:%02d:%02d', h, m, s) end
  return string.format('%d:%02d', m, s)
end

local function ass_escape(str)
  str = str:gsub('\\', '\\\226\128\139') -- a zero-width space after each backslash stops override tags
  str = str:gsub('{', '\\{'):gsub('}', '\\}'):gsub('\n', '\\N')
  return str
end

-- Cuts a UTF-8 string to at most `max` characters, with an ellipsis.
local function truncate(str, max)
  local count, cut = 0, nil
  for start in str:gmatch('()[%z\1-\127\194-\244][\128-\191]*') do
    count = count + 1
    if count == max then cut = start end
  end
  if count <= max or not cut then return str end
  return str:sub(1, cut - 1) .. '…'
end

local function language_name(lang)
  if not lang or lang == '' or lang == 'und' then return nil end
  return languages[lang:lower()] or lang:upper()
end

-- "English · AC3 · 5.1": the track's title (or language), then forced, codec, channels and external.
local function track_label(t)
  local parts = {}
  local lang = language_name(t.lang)
  local name = t.title
  if name == '' or (name and t.lang and name:lower() == t.lang:lower()) then name = nil end
  if name then
    parts[#parts + 1] = name
    if lang and not name:lower():find(lang:lower(), 1, true) then parts[#parts + 1] = lang end
  else
    parts[#parts + 1] = lang or ('Track ' .. tostring(t.id))
  end
  if t.forced then parts[#parts + 1] = 'Forced' end
  if t.codec then parts[#parts + 1] = codecs[t.codec] or t.codec:upper() end
  if t.type == 'audio' then
    local channels = t['demux-channel-count']
    if channels then parts[#parts + 1] = channel_names[channels] or (tostring(channels) .. ' ch') end
  end
  if t.external then parts[#parts + 1] = 'External' end
  return table.concat(parts, ' · ')
end

local function track_count(kind)
  local n = 0
  for _, t in ipairs(state.tracks) do
    if t.type == kind then n = n + 1 end
  end
  return n
end

local function menu_items(kind)
  local items = {}
  if kind == 'sub' then items[1] = { label = 'Off', id = 'no', selected = true } end
  for _, t in ipairs(state.tracks) do
    if t.type == kind then
      items[#items + 1] = { label = track_label(t), id = t.id, selected = t.selected == true }
      if t.selected and kind == 'sub' then items[1].selected = false end
    end
  end
  return items
end

-- Drawing --------------------------------------------------------------------------------------------------------

local function shape(ass, c, alpha)
  ass:new_event()
  ass:append(string.format('{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H%s&\\1a&H%02X&}', c, alpha or 0))
  ass:draw_start()
end

local function box(ass, x0, y0, x1, y1, c, alpha, radius)
  shape(ass, c, alpha)
  if radius and radius > 0 then
    ass:round_rect_cw(x0, y0, x1, y1, radius)
  else
    ass:rect_cw(x0, y0, x1, y1)
  end
  ass:draw_stop()
end

local function text(ass, x, y, align, size, c, str, alpha)
  ass:new_event()
  ass:append(string.format('{\\an%d\\pos(%d,%d)\\fs%d\\bord0\\shad0\\1c&H%s&\\1a&H%02X&\\fn%s}', align,
    math.floor(x), math.floor(y), math.floor(size), c, alpha or 0, mp.get_property('osd-font', 'sans-serif')))
  ass:append(ass_escape(str))
end

-- Heights of the dark gradients behind the controls, before scaling; the pointer over them shows the controls.
local bottom_shade, top_shade = 280, 130

-- A fade from the background colour: `peak` (0–255) opacity at the edge, easing to clear towards the video, so the
-- controls stay readable over a bright scene.
local function fade(ass, y0, y1, from_bottom, peak)
  local steps = 32
  local step = (y1 - y0) / steps
  for i = 0, steps - 1 do
    local strength = ((from_bottom and (i + 1) or (steps - i)) / steps) ^ 1.2
    -- Whole-pixel edges, so neighbouring strips never overlap into darker lines.
    local top, bottom = math.floor(y0 + i * step + 0.5), math.floor(y0 + (i + 1) * step + 0.5)
    if bottom > top then box(ass, 0, top, state.w, bottom, color.background, math.floor(255 - peak * strength)) end
  end
end

local function hit(x, y)
  for i = #state.elements, 1, -1 do
    local el = state.elements[i]
    if x >= el.x0 and x <= el.x1 and y >= el.y0 and y <= el.y1 then return el end
  end
  return nil
end

local function hovered(x0, y0, x1, y1)
  local m = state.mouse
  return m.hover and m.x >= x0 and m.x <= x1 and m.y >= y0 and m.y <= y1
end

local function seek_geometry()
  local s = state.s
  return 48 * s, state.w - 48 * s, state.h - 100 * s
end

local function chapter_at(t)
  local title = nil
  for _, c in ipairs(state.chapters) do
    if c.time <= t then title = c.title end
  end
  return title
end

local function draw_seekbar(ass, els)
  local s = state.s
  local x0, x1, y = seek_geometry()
  local over = state.dragging or hovered(x0, y - 16 * s, x1, y + 16 * s)
  local half = (over and 4 or 2.5) * s
  local d = state.duration or 0
  local fraction = (d > 0 and state.position) and math.max(0, math.min(1, state.position / d)) or 0
  local px = x0 + (x1 - x0) * fraction

  box(ass, x0, y - half, x1, y + half, color.highlight, 0xB0, half)
  if px > x0 then box(ass, x0, y - half, px, y + half, color.accent, 0, half) end
  if d > 0 then
    for _, c in ipairs(state.chapters) do
      if c.time > 0 and c.time < d then
        local cx = x0 + (x1 - x0) * c.time / d
        box(ass, cx - 1.5 * s, y - half, cx + 1.5 * s, y + half, color.background, 0)
      end
    end
    local knob = (over and 9 or 6) * s
    box(ass, px - knob, y - knob, px + knob, y + knob, color.highlight, 0, knob)
    els[#els + 1] = { x0 = x0, y0 = y - 16 * s, x1 = x1, y1 = y + 16 * s, seek = true }
  end

  if over and d > 0 then
    local mx = math.max(x0, math.min(x1, state.mouse.x))
    local t = d * (mx - x0) / (x1 - x0)
    local label = format_time(t)
    local chapter = chapter_at(t)
    if chapter and chapter ~= '' then label = truncate(chapter, 40) .. '  ' .. label end
    local width = (#label * 12 + 32) * s
    local bx0 = math.max(8 * s, math.min(state.w - 8 * s - width, mx - width / 2))
    box(ass, bx0, y - 62 * s, bx0 + width, y - 24 * s, color.surface, 0x10, 8 * s)
    text(ass, bx0 + width / 2, y - 43 * s, 5, 24 * s, color.highlight, label)
  end
end

-- A ring around (cx, cy) from angle a0 to a1 (radians, clockwise on screen), `thick` wide.
local function arc(ass, cx, cy, r, thick, a0, a1)
  local steps, outer, inner = 20, r + thick / 2, r - thick / 2
  for i = 0, steps do
    local a = a0 + (a1 - a0) * i / steps
    local x, y = cx + outer * math.cos(a), cy + outer * math.sin(a)
    if i == 0 then ass:move_to(x, y) else ass:line_to(x, y) end
  end
  for i = steps, 0, -1 do
    local a = a0 + (a1 - a0) * i / steps
    ass:line_to(cx + inner * math.cos(a), cy + inner * math.sin(a))
  end
end

-- The transport buttons, each drawn in colour c and alpha a within a square of half-size r around (cx, cy).
local icons = {}

function icons.play(ass, cx, cy, r, c, a)
  if state.paused then
    shape(ass, c, a)
    ass:move_to(cx - r * 0.6, cy - r * 0.8)
    ass:line_to(cx + r * 0.8, cy)
    ass:line_to(cx - r * 0.6, cy + r * 0.8)
    ass:draw_stop()
  else
    box(ass, cx - r * 0.7, cy - r * 0.8, cx - r * 0.2, cy + r * 0.8, c, a, 2 * state.s)
    box(ass, cx + r * 0.2, cy - r * 0.8, cx + r * 0.7, cy + r * 0.8, c, a, 2 * state.s)
  end
end

-- A circular arrow with the seconds inside: anticlockwise for back, clockwise for forward, open at the top where the
-- arrowhead points the way it turns.
local function skip_icon(ass, cx, cy, r, c, a, seconds, forward)
  local ring, dir = r * 0.8, forward and 1 or -1
  shape(ass, c, a)
  -- Back runs clockwise from the top round to the upper left; forward is its mirror image.
  if forward then
    arc(ass, cx, cy, ring, 2.5 * state.s, -math.pi / 6, math.pi * 3 / 2)
  else
    arc(ass, cx, cy, ring, 2.5 * state.s, -math.pi / 2, math.pi * 7 / 6)
  end
  ass:draw_stop()
  shape(ass, c, a)
  ass:move_to(cx + dir * r * 0.45, cy - ring)
  ass:line_to(cx, cy - ring - r * 0.32)
  ass:line_to(cx, cy - ring + r * 0.32)
  ass:draw_stop()
  text(ass, cx, cy + state.s, 5, r * 0.85, c, tostring(seconds), a)
end

function icons.back(ass, cx, cy, r, c, a) skip_icon(ass, cx, cy, r, c, a, opts.skip_back, false) end
function icons.forward(ass, cx, cy, r, c, a) skip_icon(ass, cx, cy, r, c, a, opts.skip_forward, true) end

-- A triangle and a bar: pointing left for the previous episode, right for the next.
local function episode_icon(ass, cx, cy, r, c, a, forward)
  local dir = forward and 1 or -1
  box(ass, cx + dir * r * 0.55 - r * 0.12, cy - r * 0.7, cx + dir * r * 0.55 + r * 0.12, cy + r * 0.7, c, a)
  shape(ass, c, a)
  ass:move_to(cx - dir * r * 0.6, cy - r * 0.7)
  ass:line_to(cx + dir * r * 0.4, cy)
  ass:line_to(cx - dir * r * 0.6, cy + r * 0.7)
  ass:draw_stop()
end

function icons.previous(ass, cx, cy, r, c, a) episode_icon(ass, cx, cy, r, c, a, false) end
function icons.next(ass, cx, cy, r, c, a) episode_icon(ass, cx, cy, r, c, a, true) end

local function skip(seconds)
  mp.commandv('seek', tostring(seconds), 'relative+exact')
end

local function choose_episode(which)
  if state.episodes and state.episodes[which] then mp.commandv('script-message', 'mediaman-episode', which) end
end

-- A label in a box ending at y, centred on cx where the window allows, like the seek bar's hover time.
local function tooltip(ass, cx, y, label)
  local s = state.s
  local width = (#label * 11 + 32) * s
  local bx0 = math.max(8 * s, math.min(state.w - 8 * s - width, cx - width / 2))
  box(ass, bx0, y - 38 * s, bx0 + width, y, color.surface, 0x10, 8 * s)
  text(ass, bx0 + width / 2, y - 19 * s, 5, 22 * s, color.highlight, label)
end

-- From the left: previous episode, skip back, play/pause, skip forward, next episode. The episode buttons show only
-- for an episode, faint where there is none that way. Returns the x where the row ends.
local function draw_transport(ass, els)
  local s = state.s
  local cy, r, gap = state.h - 46 * s, 18 * s, 58 * s
  local eps = state.episodes
  local buttons = {}
  if eps then buttons[#buttons + 1] = { icon = 'previous', episode = 'previous' } end
  buttons[#buttons + 1] = { icon = 'back', action = function() skip(-opts.skip_back) end }
  buttons[#buttons + 1] = { icon = 'play', action = function() mp.commandv('cycle', 'pause') end }
  buttons[#buttons + 1] = { icon = 'forward', action = function() skip(opts.skip_forward) end }
  if eps then buttons[#buttons + 1] = { icon = 'next', episode = 'next' } end

  local cx = 66 * s
  for _, b in ipairs(buttons) do
    local title = b.episode and eps[b.episode]
    local enabled = not b.episode or title ~= nil
    local over = enabled and hovered(cx - r, cy - r, cx + r, cy + r)
    icons[b.icon](ass, cx, cy, r, over and color.highlight or color.text, enabled and 0 or 0xB0)
    if enabled then
      local episode = b.episode
      els[#els + 1] = { x0 = cx - r, y0 = cy - r, x1 = cx + r, y1 = cy + r, action = b.action or function()
        choose_episode(episode)
      end }
    end
    if over and title then
      -- Above the seek bar, so it covers neither the bar nor the buttons.
      local _, _, bar = seek_geometry()
      tooltip(ass, cx, bar - 14 * s, (b.episode == 'next' and 'Next: ' or 'Previous: ') .. truncate(title, 60))
    end
    cx = cx + gap
  end
  return cx - gap + r
end

local function draw_menu_button(ass, els, kind, label, x1)
  local s = state.s
  local width, height = 150 * s, 40 * s
  local x0, cy = x1 - width, state.h - 46 * s
  local open = state.menu and state.menu.kind == kind
  local over = hovered(x0, cy - height / 2, x1, cy + height / 2)
  box(ass, x0, cy - height / 2, x1, cy + height / 2, (open or over) and color.accent or color.surface,
    (open or over) and 0x20 or 0x40, height / 2)
  text(ass, (x0 + x1) / 2, cy, 5, 24 * s, color.text, label)
  els[#els + 1] = { x0 = x0, y0 = cy - height / 2, x1 = x1, y1 = cy + height / 2, keep_menu = true, action = function()
    toggle_menu(kind)
  end }
  return x0
end

local function draw_bottom(ass, els)
  local s = state.s
  fade(ass, state.h - bottom_shade * s, state.h, true, 235)
  draw_seekbar(ass, els)
  local row_end = draw_transport(ass, els)

  local times = format_time(state.position)
  if state.duration and state.duration > 0 then times = times .. ' / ' .. format_time(state.duration) end
  text(ass, row_end + 22 * s, state.h - 46 * s, 4, 26 * s, color.text, times, 0x20)

  local right = state.w - 48 * s
  if track_count('sub') > 0 then right = draw_menu_button(ass, els, 'sub', 'Subtitles', right) - 12 * s end
  if track_count('audio') > 0 then draw_menu_button(ass, els, 'audio', 'Audio', right) end
end

local function choose(item)
  if not state.menu then return end
  mp.set_property(state.menu.kind == 'audio' and 'aid' or 'sid', tostring(item.id))
  toggle_menu(state.menu.kind)
end

local function draw_menu(ass, els)
  local menu = state.menu
  local s = state.s
  local row, header, width = 44 * s, 48 * s, 560 * s
  local x1 = state.w - 48 * s
  local x0 = x1 - width
  local bottom = state.h - 80 * s
  local max_rows = math.max(1, math.floor((bottom - 120 * s - header) / row))
  local first = 1
  if #menu.items > max_rows then
    first = math.max(1, math.min(menu.focus - math.floor(max_rows / 2), #menu.items - max_rows + 1))
  end
  local last = math.min(#menu.items, first + max_rows - 1)
  local top = bottom - header - (last - first + 1) * row - 12 * s

  box(ass, x0, top, x1, bottom, color.surface, 0x08, 12 * s)
  els[#els + 1] = { x0 = x0, y0 = top, x1 = x1, y1 = bottom, in_menu = true, action = function() end }
  text(ass, x0 + 24 * s, top + header / 2 + 4 * s, 4, 22 * s, color.highlight,
    menu.kind == 'audio' and 'Audio' or 'Subtitles', 0x30)

  local chars = math.floor((width - 80 * s) / (11 * s))
  for i = first, last do
    local item = menu.items[i]
    local y0 = top + header + (i - first) * row
    if i == menu.focus or hovered(x0, y0, x1, y0 + row) then
      box(ass, x0 + 8 * s, y0 + 2 * s, x1 - 8 * s, y0 + row - 2 * s, color.accent, i == menu.focus and 0x60 or 0x90, 8 * s)
    end
    if item.selected then
      local dot = 5 * s
      box(ass, x0 + 28 * s - dot, y0 + row / 2 - dot, x0 + 28 * s + dot, y0 + row / 2 + dot, color.accent, 0, dot)
    end
    text(ass, x0 + 48 * s, y0 + row / 2, 4, 24 * s, item.selected and color.highlight or color.text,
      truncate(item.label, chars))
    els[#els + 1] = { x0 = x0, y0 = y0, x1 = x1, y1 = y0 + row, in_menu = true, action = function() choose(item) end }
  end
end

local function draw_up_next(ass, els)
  local up = state.up_next
  local s = state.s
  local width, height, pad = 560 * s, 172 * s, 24 * s
  local x1 = state.w - 48 * s
  local x0 = x1 - width
  local y1 = state.h - 150 * s
  local y0 = y1 - height
  local remaining = math.max(0, up.deadline - mp.get_time())

  box(ass, x0, y0, x1, y1, color.surface, 0x08, 12 * s)
  els[#els + 1] = { x0 = x0, y0 = y0, x1 = x1, y1 = y1, keep_menu = true, action = function() end }
  text(ass, x0 + pad, y0 + pad + 10 * s, 4, 20 * s, color.highlight, 'Up next', 0x30)
  text(ass, x0 + pad, y0 + pad + 46 * s, 4, 26 * s, color.text, truncate(up.title, math.floor((width - 2 * pad) / (13 * s))))

  local bw, bh = 136 * s, 44 * s
  local by0, by1 = y1 - pad - bh, y1 - pad
  text(ass, x0 + pad, (by0 + by1) / 2, 4, 20 * s, color.highlight,
    string.format('Starting in %d s', math.ceil(remaining)), 0x30)
  local buttons = {
    { label = 'Play now', x1 = x1 - pad, fill = color.accent, choice = 'play' },
    { label = 'Cancel', x1 = x1 - pad - bw - 12 * s, fill = color.background, choice = 'cancel' },
  }
  for _, b in ipairs(buttons) do
    local bx0 = b.x1 - bw
    local hot = hovered(bx0, by0, b.x1, by1)
    box(ass, bx0, by0, b.x1, by1, b.fill, hot and 0x00 or 0x30, 8 * s)
    text(ass, (bx0 + b.x1) / 2, (by0 + by1) / 2, 5, 22 * s, hot and color.highlight or color.text, b.label)
    els[#els + 1] = { x0 = bx0, y0 = by0, x1 = b.x1, y1 = by1, keep_menu = true, action = function()
      finish_up_next(b.choice)
    end }
  end

  -- How much of the countdown is left, along the bottom edge of the card.
  local track = width - 24 * s
  box(ass, x0 + 12 * s, y1 - 6 * s, x0 + 12 * s + track * remaining / up.seconds, y1 - 3 * s, color.accent, 0)
end

-- Visibility and rendering ---------------------------------------------------------------------------------------

local function over_controls()
  local m = state.mouse
  return m.hover and (m.y >= state.h - bottom_shade * state.s or m.y <= top_shade * state.s)
end

local function visible()
  return state.paused or state.menu ~= nil or state.dragging or mp.get_time() < state.show_until or over_controls()
end

local function update_click_binding()
  local m = state.mouse
  local want = state.dragging or (#state.elements > 0 and m.hover and (state.menu ~= nil or hit(m.x, m.y) ~= nil))
  if want == state.click_bound then return end
  state.click_bound = want
  if want then
    mp.add_forced_key_binding('MBTN_LEFT', 'mediaman-osc-click', function(e) on_click(e) end, { complex = true })
    -- A double click on the controls shouldn't also toggle fullscreen.
    mp.add_forced_key_binding('MBTN_LEFT_DBL', 'mediaman-osc-dbl', function() end)
  else
    mp.remove_key_binding('mediaman-osc-click')
    mp.remove_key_binding('mediaman-osc-dbl')
  end
end

local render_pending = false

local function render()
  render_pending = false
  local w, h = mp.get_osd_size()
  if not w or w <= 0 or h <= 0 then return end
  state.w, state.h = w, h
  state.s = math.max(0.6, math.min(h / 1080, 2.5)) * opts.scale
  local controls = visible()
  if not controls and not state.up_next then
    overlay:remove()
    state.elements = {}
    update_click_binding()
    return
  end

  local ass = assdraw.ass_new()
  local els = {}
  if controls then
    fade(ass, 0, top_shade * state.s, false, 170)
    text(ass, 48 * state.s, 46 * state.s, 4, 34 * state.s, color.text,
      truncate(state.title, math.max(10, math.floor((w - 96 * state.s) / (17 * state.s)))))
    draw_bottom(ass, els)
  end
  if state.up_next then draw_up_next(ass, els) end
  if controls and state.menu then draw_menu(ass, els) end
  state.elements = els

  overlay.res_x, overlay.res_y = w, h
  overlay.data = ass.text
  overlay:update()
  update_click_binding()
end

local function request_render()
  if render_pending then return end
  render_pending = true
  mp.add_timeout(0.03, render)
end

local hide_timer = mp.add_timeout(opts.hide_timeout, request_render)
hide_timer:kill()

-- Shows the controls for `hide_timeout` seconds from now.
local function poke()
  state.show_until = mp.get_time() + opts.hide_timeout
  hide_timer:kill()
  hide_timer:resume()
  request_render()
end

-- Input ----------------------------------------------------------------------------------------------------------

local function seek_to_mouse(exact)
  local x0, x1 = seek_geometry()
  local pct = 100 * math.max(0, math.min(1, (state.mouse.x - x0) / (x1 - x0)))
  mp.commandv('seek', tostring(pct), exact and 'absolute-percent+exact' or 'absolute-percent+keyframes')
end

local menu_keys = { 'mediaman-osc-up', 'mediaman-osc-down', 'mediaman-osc-enter', 'mediaman-osc-kp-enter', 'mediaman-osc-esc' }

local function move_focus(delta)
  local menu = state.menu
  if not menu or #menu.items == 0 then return end
  menu.focus = (menu.focus - 1 + delta) % #menu.items + 1
  request_render()
end

local function close_menu()
  if not state.menu then return end
  state.menu = nil
  for _, name in ipairs(menu_keys) do mp.remove_key_binding(name) end
  poke()
end

local function open_menu(kind)
  local items = menu_items(kind)
  local focus = 1
  for i, item in ipairs(items) do
    if item.selected then focus = i end
  end
  state.menu = { kind = kind, items = items, focus = focus }
  local function pick()
    local item = state.menu and state.menu.items[state.menu.focus]
    if item then choose(item) end
  end
  mp.add_forced_key_binding('UP', 'mediaman-osc-up', function() move_focus(-1) end, { repeatable = true })
  mp.add_forced_key_binding('DOWN', 'mediaman-osc-down', function() move_focus(1) end, { repeatable = true })
  mp.add_forced_key_binding('ENTER', 'mediaman-osc-enter', pick)
  mp.add_forced_key_binding('KP_ENTER', 'mediaman-osc-kp-enter', pick)
  mp.add_forced_key_binding('ESC', 'mediaman-osc-esc', close_menu)
  poke()
end

toggle_menu = function(kind)
  local was = state.menu and state.menu.kind
  close_menu()
  if was ~= kind then open_menu(kind) end
end

on_click = function(e)
  if e.event == 'down' then
    local el = hit(state.mouse.x, state.mouse.y)
    if not el then
      close_menu()
    elseif el.seek then
      close_menu()
      state.dragging = true
      seek_to_mouse(false)
    else
      if not el.keep_menu and not el.in_menu then close_menu() end
      el.action()
    end
    request_render()
  elseif e.event == 'up' and state.dragging then
    state.dragging = false
    seek_to_mouse(true)
    poke()
  end
end

-- Up next --------------------------------------------------------------------------------------------------------

local up_next_keys = { 'mediaman-up-next-enter', 'mediaman-up-next-kp-enter', 'mediaman-up-next-esc' }

finish_up_next = function(choice)
  local up = state.up_next
  if not up then return end
  up.timer:kill()
  state.up_next = nil
  for _, name in ipairs(up_next_keys) do mp.remove_key_binding(name) end
  mp.commandv('script-message', 'mediaman-up-next-choice', choice)
  request_render()
end

mp.register_script_message('mediaman-up-next', function(seconds, title)
  if state.up_next then return end
  local total = math.max(1, math.min(60, math.floor(tonumber(seconds) or 10)))
  -- `from`: where the card appeared; a seek landing before it means the viewer went back to keep watching.
  local up = { title = title or '', seconds = total, deadline = mp.get_time() + total, from = mp.get_property_number('time-pos') }
  up.timer = mp.add_periodic_timer(0.25, function()
    if mp.get_time() >= up.deadline then finish_up_next('play') else request_render() end
  end)
  state.up_next = up
  local function play() finish_up_next('play') end
  mp.add_forced_key_binding('ENTER', 'mediaman-up-next-enter', play)
  mp.add_forced_key_binding('KP_ENTER', 'mediaman-up-next-kp-enter', play)
  mp.add_forced_key_binding('ESC', 'mediaman-up-next-esc', function() finish_up_next('cancel') end)
  request_render()
end)

-- Episodes ---------------------------------------------------------------------------------------------------------

mp.register_script_message('mediaman-episodes', function(previous, following)
  local function title(v) return (v and v ~= '') and v or nil end
  state.episodes = { previous = title(previous), next = title(following) }
  request_render()
end)

mp.add_key_binding('PREV', 'previous-episode', function() choose_episode('previous') end)
mp.add_key_binding('NEXT', 'next-episode', function() choose_episode('next') end)
mp.add_key_binding(nil, 'skip-back', function() skip(-opts.skip_back) end, { repeatable = true })
mp.add_key_binding(nil, 'skip-forward', function() skip(opts.skip_forward) end, { repeatable = true })

mp.add_key_binding('a', 'audio-menu', function() toggle_menu('audio') end)
mp.add_key_binding('u', 'subtitle-menu', function() toggle_menu('sub') end)

-- Properties -----------------------------------------------------------------------------------------------------

mp.observe_property('mouse-pos', 'native', function(_, m)
  if not m then return end
  local moved = m.x ~= state.mouse.x or m.y ~= state.mouse.y
  state.mouse = m
  if not m.hover then
    -- The button-up may never arrive once the pointer has left the window, so a drag ends here.
    state.dragging = false
    state.show_until = 0
  elseif moved then
    poke()
  end
  if state.dragging then seek_to_mouse(false) end
  request_render()
end)

mp.observe_property('pause', 'bool', function(_, paused)
  state.paused = paused == true
  poke()
end)

mp.observe_property('time-pos', 'number', function(_, v)
  state.position = v
  if visible() then request_render() end
end)

mp.observe_property('duration', 'number', function(_, v)
  state.duration = v
  request_render()
end)

mp.observe_property('media-title', 'string', function(_, v)
  state.title = v or ''
  request_render()
end)

mp.observe_property('chapter-list', 'native', function(_, v)
  state.chapters = v or {}
  request_render()
end)

mp.observe_property('track-list', 'native', function(_, v)
  state.tracks = v or {}
  local menu = state.menu
  if menu then
    menu.items = menu_items(menu.kind)
    menu.focus = math.max(1, math.min(menu.focus, #menu.items))
  end
  request_render()
end)

mp.observe_property('osd-dimensions', 'native', request_render)
mp.register_event('seek', poke)
-- Only a seek that has landed before the card cancels it: a click on the seek bar seeks twice (on press, then exactly
-- on release) and a drag many times, so the card can arrive between two seeks into the credits and must survive them.
-- While the bar is held the keyframe seeks may land early, so only the exact seek on release (or a key seek) decides.
mp.register_event('playback-restart', function()
  local up = state.up_next
  if not up or not up.from or state.dragging then return end
  local pos = mp.get_property_number('time-pos')
  if pos and pos < up.from - 1 then finish_up_next('cancel') end
end)
mp.register_event('file-loaded', poke)
