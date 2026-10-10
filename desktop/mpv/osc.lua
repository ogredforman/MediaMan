-- MediaMan on-screen controller (SPEC §7.4 step 3, §8 playback [M], §11): the title, a progress bar with chapters,
-- play/pause and time, audio and subtitle menus, and the "Up next" countdown, in the purple theme. Loaded with --script by the desktop app,
-- with the built-in OSC off (mpv.conf). It talks to the desktop main process only through MPV script messages and
-- properties (.claude/rules/desktop.md). Needs MPV 0.37 or later (the mouse-pos property).
--
-- Keys: `a` opens the audio menu and `u` the subtitle menu (rebind with `script-binding osc/audio-menu` and
-- `script-binding osc/subtitle-menu` in input.conf); in a menu, Up/Down move, Enter picks and Esc closes.
--
-- Up next (SPEC §7.4 step 5): the main process sends `script-message mediaman-up-next <seconds> <title>`; the card
-- counts down and answers `script-message mediaman-up-next-choice play` when it runs out or on Play now (Enter), or
-- `... cancel` on Cancel (Esc) or a seek.

local assdraw = require 'mp.assdraw'
local options = require 'mp.options'

local opts = {
  hide_timeout = 2, -- seconds without mouse movement before the controls hide
  scale = 1, -- multiplies the size taken from the window height
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

-- A soft fade from the background colour: opaque-ish at the edge, clear towards the video.
local function fade(ass, y0, y1, from_bottom)
  local steps = 24
  local step = (y1 - y0) / steps
  for i = 0, steps - 1 do
    local strength = (from_bottom and (i + 1) or (steps - i)) / steps
    -- Whole-pixel edges, so neighbouring strips never overlap into darker lines.
    local top, bottom = math.floor(y0 + i * step + 0.5), math.floor(y0 + (i + 1) * step + 0.5)
    if bottom > top then box(ass, 0, top, state.w, bottom, color.background, math.floor(255 - 150 * strength)) end
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

local function draw_play_button(ass, els)
  local s = state.s
  local cx, cy, r = 66 * s, state.h - 46 * s, 18 * s
  local c = hovered(cx - r, cy - r, cx + r, cy + r) and color.highlight or color.text
  if state.paused then
    shape(ass, c, 0)
    ass:move_to(cx - r * 0.6, cy - r * 0.8)
    ass:line_to(cx + r * 0.8, cy)
    ass:line_to(cx - r * 0.6, cy + r * 0.8)
    ass:draw_stop()
  else
    box(ass, cx - r * 0.7, cy - r * 0.8, cx - r * 0.2, cy + r * 0.8, c, 0, 2 * s)
    box(ass, cx + r * 0.2, cy - r * 0.8, cx + r * 0.7, cy + r * 0.8, c, 0, 2 * s)
  end
  els[#els + 1] = { x0 = cx - r, y0 = cy - r, x1 = cx + r, y1 = cy + r, action = function()
    mp.commandv('cycle', 'pause')
  end }
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
  fade(ass, state.h - 200 * s, state.h, true)
  draw_seekbar(ass, els)
  draw_play_button(ass, els)

  local times = format_time(state.position)
  if state.duration and state.duration > 0 then times = times .. ' / ' .. format_time(state.duration) end
  text(ass, 104 * s, state.h - 46 * s, 4, 26 * s, color.text, times, 0x20)

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
  return m.hover and (m.y >= state.h - 200 * state.s or m.y <= 110 * state.s)
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
    fade(ass, 0, 110 * state.s, false)
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
  local up = { title = title or '', seconds = total, deadline = mp.get_time() + total }
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
mp.register_event('seek', function()
  -- Seeking means the viewer wants to keep watching this one.
  finish_up_next('cancel')
  poke()
end)
mp.register_event('file-loaded', poke)
