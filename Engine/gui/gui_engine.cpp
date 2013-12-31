//=============================================================================
//
// Adventure Game Studio (AGS)
//
// Copyright (C) 1999-2011 Chris Jones and 2011-20xx others
// The full list of copyright holders can be found in the Copyright.txt
// file, which is part of this source code distribution.
//
// The AGS source code is provided under the Artistic License 2.0.
// A copy of this license can be found in the file License.txt and at
// http://www.opensource.org/licenses/artistic-license-2.0.php
//
//=============================================================================
//
// Implementation from acgui.h and acgui.cpp specific to Engine runtime
//
//=============================================================================

// Headers, as they are in acgui.cpp
#pragma unmanaged
#include "ac/game_version.h"
#include "font/fonts.h"
#include "game/game_objects.h"
#include "gui/guimain.h"
#include "gui/guibutton.h"
#include "gui/guilabel.h"
#include "gui/guilistbox.h"
#include "gui/guitextbox.h"
#include <ctype.h>
#include "ac/global_translation.h"
#include "ac/string.h"
#include "ac/spritecache.h"
#include "gfx/bitmap.h"
#include "gfx/blender.h"

using AGS::Common::Bitmap;
using AGS::Common::GuiButton;
using AGS::Common::GuiLabel;
using AGS::Common::GuiListBox;
using AGS::Common::GuiObject;
using AGS::Common::GuiTextBox;
using AGS::Common::String;

// For engine these are defined in ac.cpp
extern int eip_guiobj;
extern void replace_macro_tokens(const char *text, String &fixed_text);

extern SpriteCache spriteset; // in ac_runningame

bool GUIMain::is_alpha() 
{
    if (this->bgpic > 0)
    {
        // alpha state depends on background image
        return is_sprite_alpha(this->bgpic);
    }
    if (this->bgcol > 0)
    {
        // not alpha transparent if there is a background color
        return false;
    }
    // transparent background, enable alpha blending
    return final_col_dep >= 24 &&
        // transparent background have alpha channel only since 3.2.0;
        // "classic" gui rendering mode historically had non-alpha transparent backgrounds
        // (3.2.0 broke the compatibility, now we restore it)
        loaded_game_file_version >= kGameVersion_320 && game.options[OPT_NEWGUIALPHA] != kGuiAlphaRender_Classic;
}

//=============================================================================
// Engine-specific implementation split out of acgui.h
//=============================================================================

bool GuiObject::IsClickable() const
{
  return !(Flags & kGuiCtrl_NoClicks);
}

void check_font(int *fontnum)
{
    // do nothing
}

//=============================================================================
// Engine-specific implementation split out of acgui.cpp
//=============================================================================

int get_adjusted_spritewidth(int spr)
{
  return spriteset[spr]->GetWidth();
}

int get_adjusted_spriteheight(int spr)
{
  return spriteset[spr]->GetHeight();
}

bool is_sprite_alpha(int spr)
{
  return ((game.SpriteFlags[spr] & SPF_ALPHACHANNEL) != 0);
}

void set_eip_guiobj(int eip)
{
  eip_guiobj = eip;
}

int get_eip_guiobj()
{
  return eip_guiobj;
}

bool outlineGuiObjects = false;

void GuiButton::PrepareTextToDraw()
{
  // Allow it to change the string to unicode if it's TTF
    if (Flags & kGuiCtrl_Translated)
    {
        TextToDraw.SetString(get_translation(Text));
    }
    else
    {
        TextToDraw.SetString(Text);
    }
    // FIXME this hack!
    char *buffer = const_cast<char*>(TextToDraw.GetCStr());
    ensure_text_valid_for_font(buffer, TextFont);
}


void GuiLabel::PrepareTextToDraw()
{
    replace_macro_tokens(Flags & kGuiCtrl_Translated ? get_translation(Text) : Text, TextToDraw);
    // FIXME this hack!
    char *buffer = const_cast<char*>(TextToDraw.GetCStr());
    ensure_text_valid_for_font(buffer, TextFont);
}

int GuiLabel::SplitLinesForDrawing()
{
    // Use the engine's word wrap tool, to have hebrew-style writing
    // and other features
    break_up_text_into_lines(Frame.GetWidth(), TextFont, TextToDraw);
    return numlines;
}

void GuiListBox::DrawItemsFix()
{
    // do nothing
}

void GuiListBox::DrawItemsUnfix()
{
    // do nothing
}

void GuiListBox::PrepareTextToDraw(const String &text)
{
    // Allow it to change the string to unicode if it's TTF
    if (Flags & kGuiCtrl_Translated)
    {
        TextToDraw.SetString(get_translation(text));
    }
    else
    {
        TextToDraw.SetString(text);
    }
    // FIXME this hack!
    char *buffer = const_cast<char*>(TextToDraw.GetCStr());
    ensure_text_valid_for_font(buffer, TextFont);
}

void GuiTextBox::DrawTextBoxContents(Bitmap *ds, color_t text_color)
{
    wouttext_outline(ds, Frame.Left + 1 + get_fixed_pixel_size(1), Frame.Top + 1 + get_fixed_pixel_size(1), TextFont, text_color, Text);
    if (!IsDisabled())
    {
        // draw a cursor
        Point draw_at;
        draw_at.X = wgettextwidth(Text, TextFont) + Frame.Left + 3;
        draw_at.Y = Frame.Top + 1 + wgettextheight("BigyjTEXT", TextFont);
        ds->DrawRect(Rect(draw_at.X, draw_at.Y, draw_at.X + get_fixed_pixel_size(5), draw_at.Y + (get_fixed_pixel_size(1) - 1)), text_color);
    }
}
