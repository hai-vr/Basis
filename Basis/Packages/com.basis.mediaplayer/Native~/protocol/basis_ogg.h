/* Ogg demuxer -> Opus audio into a basis_media_sink. Parses `.opus` files: the
 * OpusHead identification header, OpusTags (skipped), then Opus audio packets
 * reassembled across pages. Forward playback only in v1 (no granulepos seek), so
 * no duration is reported. Pulls bytes through the supplied read callback. */
#ifndef BASIS_OGG_H
#define BASIS_OGG_H

#include "../basis_media_internal.h"

#ifdef __cplusplus
extern "C" {
#endif

int basis_ogg_run(basis_media_sink_t* sink, basis_read_fn read, void* ctx,
                  basis_reseek_fn reseek, void* reseek_ctx);

#ifdef __cplusplus
}
#endif
#endif
