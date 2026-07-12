#!/usr/bin/env python3
"""
Generate a looping MPEG-TS test clip carrying in-band CEA-608 (CC1) captions, to
exercise the media player's caption decoder end to end.

ffmpeg can read/repack captions but not synthesise them, so this builds the
caption bytes itself: it encodes pop-on CEA-608 command sequences, wraps them in
ATSC A/53 (GA94) cc_data SEI NAL units, and splices one in at each caption-change
frame of an H.264 Annex B elementary stream produced by ffmpeg. A second ffmpeg
pass muxes the result to MPEG-TS.

Usage:  gen_cc_ts.py [output.ts]        (default: test_cc.ts beside this script)
Verify: ffprobe -show_frames output.ts | grep -i caption   ("ATSC A53" side data)
"""
import os
import subprocess
import sys
import tempfile

FPS = 30
DUR = 20            # seconds
W, H = 854, 480

# Schedule: (frame_index, caption_text or None-for-clear). Pop-on; the cue persists
# until the next entry, so a change every ~3s reads comfortably.
SCHEDULE = [
    (0,   "CEA-608 CAPTION TEST"),
    (90,  "THE QUICK BROWN FOX"),
    (180, "JUMPS OVER THE LAZY DOG"),
    (270, None),                       # clear
    (330, "ACCENTS: cafe résumé piñata"),
    (420, "MUSIC: ♪ la la la ♪"),
    (510, "LAST LINE - LOOP RESTARTS"),
]

# ---- CEA-608 encoding ----------------------------------------------------

def odd_parity(b):
    b &= 0x7F
    ones = bin(b).count("1")
    return b | (0x00 if ones % 2 else 0x80)

# Basic North American set: accented characters with dedicated codepoints.
BASIC = {"á": 0x2A, "é": 0x5C, "í": 0x5E, "ó": 0x5F, "ú": 0x60,
         "ç": 0x7B, "÷": 0x7C, "Ñ": 0x7D, "ñ": 0x7E}
# Special chars live in a control pair (0x11, 0x30-0x3F).
SPECIAL = {"♪": 0x37}   # music note

def char_pairs(text):
    """Yield (b0,b1) byte pairs for the text body (no parity yet)."""
    pending = []
    out = []
    for ch in text:
        if ch in SPECIAL:                 # special char is its own control pair
            if len(pending) == 1:
                out.append((pending[0], 0x00)); pending = []
            out.append((0x11, SPECIAL[ch]))
            continue
        code = BASIC.get(ch)
        if code is None:
            o = ord(ch)
            code = o if 0x20 <= o <= 0x7F else 0x20   # fall back to space
        pending.append(code)
        if len(pending) == 2:
            out.append((pending[0], pending[1])); pending = []
    if pending:
        out.append((pending[0], 0x00))
    return out

RCL = (0x14, 0x20); ENM = (0x14, 0x2E); EDM = (0x14, 0x2C); EOC = (0x14, 0x2F)
PAC_R15 = (0x14, 0x60)   # row 15, white, column 0

def encode_caption(text):
    if text is None:
        return [EDM]
    return [RCL, ENM, PAC_R15] + char_pairs(text) + [EOC]

# ---- SEI / NAL assembly --------------------------------------------------

def cc_data(pairs):
    n = len(pairs)
    assert n <= 31
    b = bytearray()
    b.append(0xC0 | n)        # reserved=1, process_cc_data_flag=1, additional=0, cc_count
    b.append(0xFF)            # em_data
    for d1, d2 in pairs:
        b.append(0xFC)        # marker(11111) | cc_valid(1) | cc_type(00 = 608 field 1)
        b.append(odd_parity(d1))
        b.append(odd_parity(d2))
    b.append(0xFF)            # trailing marker
    return bytes(b)

def user_data(pairs):
    p = bytearray()
    p += bytes([0xB5, 0x00, 0x31])          # country USA, provider ATSC
    p += b"GA94"                            # user_identifier
    p.append(0x03)                          # user_data_type_code = cc_data
    p += cc_data(pairs)
    return bytes(p)

def emulation_escape(rbsp):
    out = bytearray()
    zeros = 0
    for byte in rbsp:
        if zeros >= 2 and byte <= 0x03:
            out.append(0x03)
            zeros = 0
        out.append(byte)
        zeros = zeros + 1 if byte == 0 else 0
    return bytes(out)

def sei_nal(pairs):
    payload = user_data(pairs)
    msg = bytearray()
    msg.append(0x04)                        # payloadType = user_data_registered_itu_t_t35
    size = len(payload)
    while size >= 255:
        msg.append(0xFF); size -= 255
    msg.append(size)
    msg += payload
    rbsp = bytes(msg) + b"\x80"             # rbsp_trailing_bits
    return b"\x00\x00\x00\x01" + b"\x06" + emulation_escape(rbsp)

# ---- Annex B splicing ----------------------------------------------------

def find_nals(data):
    """Yield (start_index_of_startcode, nal_type) for each NAL."""
    i, n = 0, len(data)
    while i + 3 < n:
        if data[i] == 0 and data[i+1] == 0 and data[i+2] == 1:
            sc = 3
        elif data[i] == 0 and data[i+1] == 0 and data[i+2] == 0 and data[i+3] == 1:
            sc = 4
        else:
            i += 1; continue
        nal_type = data[i+sc] & 0x1F
        yield (i, nal_type)
        i += sc + 1

def splice(data, schedule):
    """Insert an SEI NAL into each scheduled access unit, in AU order: after
    AUD/SPS/PPS and immediately before the first VCL slice NAL."""
    by_frame = dict(schedule)
    nals = list(find_nals(data))
    out = bytearray()
    au_index = -1
    pending = None                          # caption pairs awaiting the slice NAL
    for k, (off, ntype) in enumerate(nals):
        end = nals[k+1][0] if k + 1 < len(nals) else len(data)
        if ntype == 9:                      # AUD => start of a new access unit
            au_index += 1
            pending = encode_caption(by_frame[au_index]) if au_index in by_frame else None
        if 1 <= ntype <= 5 and pending is not None:
            out += sei_nal(pending)         # prefix SEI sits before the coded picture
            pending = None
        out += data[off:end]
    return bytes(out)

# ---- pipeline ------------------------------------------------------------

def run(cmd):
    print("+", " ".join(cmd))
    subprocess.run(cmd, check=True)

def main():
    out_path = sys.argv[1] if len(sys.argv) > 1 else \
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_cc.ts")
    with tempfile.TemporaryDirectory() as tmp:
        base = os.path.join(tmp, "base.h264")
        mod = os.path.join(tmp, "mod.h264")
        # No B-frames (bframes=0): the raw-H.264 demuxer in the copy pass can't
        # derive DTS for reordered frames, which makes the MPEG-TS muxer reject
        # the stream.
        run(["ffmpeg", "-y", "-f", "lavfi", "-i", f"testsrc2=s={W}x{H}:r={FPS}:d={DUR}",
             "-pix_fmt", "yuv420p", "-c:v", "libx264", "-profile:v", "baseline",
             "-g", str(FPS), "-keyint_min", str(FPS), "-bf", "0",
             "-x264-params", "aud=1:scenecut=0", "-f", "h264", base])
        data = open(base, "rb").read()
        spliced = splice(data, SCHEDULE)
        open(mod, "wb").write(spliced)
        print(f"  spliced {len(SCHEDULE)} caption SEIs ({len(data)} -> {len(spliced)} bytes)")
        run(["ffmpeg", "-y", "-framerate", str(FPS), "-i", mod, "-c", "copy",
             "-muxrate", "3M", "-f", "mpegts", out_path])
    print(f"\nWrote {out_path}")

if __name__ == "__main__":
    main()
