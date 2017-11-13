# Current errors/warnings to be fixed for Algernon 5


```
make[1]: Entering directory '/home/afr/clones/ags/Engine'
CFLAGS = -I/usr/include/allegro5 -O2 -g -fsigned-char -Wfatal-errors -DNDEBUG -DALLEGRO_SRC -DAGS_RUNTIME_PATCH_ALLEGRO -DAGS_HAS_CD_AUDIO -DAGS_CASE_SENSITIVE_FILESYSTEM -DALLEGRO_STATICLINK -DLINUX_VERSION -DDISABLE_MPEG_AUDIO -DBUILTIN_PLUGINS -DRTLD_NEXT -I/usr/include/freetype2 -I/usr/include/libpng16 -I/usr/include/harfbuzz -I/usr/include/glib-2.0 -I/usr/lib/glib-2.0/include -I../Engine -I../Common -I../Common/libinclude -I../Plugins \n
CXXFLAGS = -fno-rtti -Wno-write-strings -I/usr/include/allegro5 -O2 -g -fsigned-char -Wfatal-errors -DNDEBUG -DALLEGRO_SRC -DAGS_RUNTIME_PATCH_ALLEGRO -DAGS_HAS_CD_AUDIO -DAGS_CASE_SENSITIVE_FILESYSTEM -DALLEGRO_STATICLINK -DLINUX_VERSION -DDISABLE_MPEG_AUDIO -DBUILTIN_PLUGINS -DRTLD_NEXT -I/usr/include/freetype2 -I/usr/include/libpng16 -I/usr/include/harfbuzz -I/usr/include/glib-2.0 -I/usr/lib/glib-2.0/include -I../Engine -I../Common -I../Common/libinclude -I../Plugins \n
LDFLAGS = -Wl,--as-needed \n
LIBS = -rdynamic -laldmb -ldumb -Wl,-Bdynamic -lallegro -lX11 -logg -ltheora -logg -lvorbis -lvorbisfile -lfreetype -ldl -lpthread -lc -lm -lstdc++ \n
libsrc/alfont-2.0.9/alfont.o
libsrc/alfont-2.0.9/alfont.c: In function ‘set_preservedalpha_trans_blender’:
libsrc/alfont-2.0.9/alfont.c:166:3: warning: implicit declaration of function ‘set_blender_mode’; did you mean ‘al_set_blender’? [-Wimplicit-function-declaration]
   set_blender_mode(__skiptranspixels_blender_trans15, __skiptranspixels_blender_trans16, __preservedalpha_blender_trans24, r, g, b, a);
   ^~~~~~~~~~~~~~~~
   al_set_blender
libsrc/alfont-2.0.9/alfont.c: In function ‘alfont_textout_aa_ex’:
libsrc/alfont-2.0.9/alfont.c:748:16: warning: implicit declaration of function ‘get_uformat’; did you mean ‘getsubopt’? [-Wimplicit-function-declaration]
   curr_uformat=get_uformat();
                ^~~~~~~~~~~
                getsubopt
libsrc/alfont-2.0.9/alfont.c:831:3: warning: implicit declaration of function ‘set_uformat’ [-Wimplicit-function-declaration]
   set_uformat(U_UNICODE);
   ^~~~~~~~~~~
libsrc/alfont-2.0.9/alfont.c:956:3: warning: implicit declaration of function ‘drawing_mode’ [-Wimplicit-function-declaration]
   drawing_mode(DRAW_MODE_TRANS,NULL,0,0);
   ^~~~~~~~~~~~
libsrc/alfont-2.0.9/alfont.c:956:16: error: ‘DRAW_MODE_TRANS’ undeclared (first use in this function)
   drawing_mode(DRAW_MODE_TRANS,NULL,0,0);
                ^~~~~~~~~~~~~~~
compilation terminated due to -Wfatal-errors.
make[1]: *** [Makefile:42: libsrc/alfont-2.0.9/alfont.o] Error 1
make[1]: Leaving directory '/home/afr/clones/ags/Engine'
make: *** [Makefile:4: all] Error 2
```
