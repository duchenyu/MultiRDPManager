#ifndef WINPR_BUILD_FLAGS_H
#define WINPR_BUILD_FLAGS_H

#define WINPR_CFLAGS "-march=nocona -msahf -mtune=generic -O2 -pipe -Wp,-D_FORTIFY_SOURCE=2 -fstack-protector-strong -Wp,-D__USE_MINGW_ANSI_STDIO=1 -Wno-deprecated-declarations -Wno-incompatible-pointer-types -D__STDC_NO_THREADS__=1 -fvisibility=hidden -fno-omit-frame-pointer -Wredundant-decls -fsigned-char -Wimplicit-function-declaration -Wno-jump-misses-init -fvisibility=hidden -O3 -DNDEBUG"
#define WINPR_COMPILER_ID "GNU"
#define WINPR_COMPILER_VERSION "16.1.0"
#define WINPR_TARGET_ARCH "x64"
#define WINPR_BUILD_CONFIG ""
#define WINPR_BUILD_TYPE "Release"

#endif /* WINPR_BUILD_FLAGS_H */
