// Alpine component for a full-screen photo viewer with prev/next navigation.
// Used by both the guest gallery and the admin event-detail "Recent uploads"
// grid. Each page renders its own thumbnail grid but shares the overlay
// markup in _GalleryLightbox.cshtml and this component definition.
window.galleryLightbox = function ({ items }) {
    return {
        items,
        index: null,
        open(i) {
            this.index = i;
            document.body.style.overflow = "hidden";
        },
        close() {
            this.index = null;
            document.body.style.overflow = "";
        },
        next() {
            if (this.index === null || this.items.length === 0) return;
            this.index = (this.index + 1) % this.items.length;
        },
        prev() {
            if (this.index === null || this.items.length === 0) return;
            this.index = (this.index - 1 + this.items.length) % this.items.length;
        },
        addItem(item) {
            if (!item || !item.id) return;
            if (this.items.some((x) => x.id === item.id)) return;
            this.items.unshift({
                id: item.id,
                kind: item.kind ?? 0,
                uploaderName: item.uploaderName ?? null,
            });
            // Keep the currently-viewed photo in focus after a prepend.
            if (this.index !== null) this.index += 1;
        },
    };
};
