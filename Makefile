.PHONY: clean

all:
	@make -C Engine

install:
	install -Dm755 Engine/ags "$(DESTDIR)/$(PREFIX)/bin/ags"
	install -Dm644 LICENSE "$(DESTDIR)/$(PREFIX)/share/licenses/ags/LICENSE"

clean:
	@make -C Engine clean
