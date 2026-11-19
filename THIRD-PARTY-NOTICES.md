# Third-party notices

Pomegranate's own code is MIT-licensed (see LICENSE). It bundles the components below, each under its own license.

## FFmpeg 9.0.2 (GPL)

Pomegranate runs FFmpeg (`ffmpeg.exe`, `ffprobe.exe` and their DLLs) as a separate program for audio and video conversions. The bundled build is BtbN's "gpl-shared" build, licensed under the GNU General Public License version 3 or later; the full text ships as `engines/ffmpeg/LICENSE.txt`.

- FFmpeg project and source: https://ffmpeg.org/download.html
- Build scripts and the exact build used: https://github.com/BtbN/FFmpeg-Builds (release `autobuild-2026-11-18-12-39`, `ffmpeg-n9.0.2-57-gd838f9f9ae`)

## dnglab 0.8.0 (LGPL-2.1)

Runs as a separate program to convert camera RAW files to DNG. Source: https://github.com/dnglab/dnglab

## Magick.NET 15.1.0 (Apache-2.0) and ImageMagick 7.1.2 (ImageMagick License)

Used in-process for image conversions. Magick.NET: https://github.com/dlemstra/Magick.NET. ImageMagick license: https://imagemagick.org/script/license.php. The native ImageMagick library includes other open-source libraries (for example libheif, libraw, librsvg, libwebp, libaom, libjpeg-turbo, libpng, libtiff and zlib), each under its own license; the Magick.NET repository lists them.

## SharpCompress 0.50.10 (MIT)

Used in-process to read archives. https://github.com/adamhathcock/sharpcompress
