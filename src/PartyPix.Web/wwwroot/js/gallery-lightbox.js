// Alpine component for a full-screen photo viewer with prev/next navigation.
// Used by both the guest gallery and the admin event-detail "Recent uploads"
// grid. Each page renders its own thumbnail grid but shares the overlay
// markup in _GalleryLightbox.cshtml and this component definition.
window.galleryLightbox = function ({ items, canDelete = false }) {
    return {
        items,
        index: null,
        canDelete,
        deleting: false,
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
        async remove() {
            if (!this.canDelete || this.index === null) return;
            const item = this.items[this.index];
            if (!item) return;
            if (!confirm("Delete this photo from the server? This cannot be undone.")) return;

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            this.deleting = true;
            try {
                const resp = await fetch("/media/" + item.id, {
                    method: "DELETE",
                    headers: token ? { "X-CSRF-TOKEN": token } : {},
                });
                if (!resp.ok) {
                    alert("Delete failed (" + resp.status + ").");
                    return;
                }
                const removedIndex = this.index;
                this.items.splice(removedIndex, 1);
                if (this.items.length === 0) {
                    this.close();
                } else {
                    // Stay on the same position, clamped to the new end.
                    this.index = Math.min(removedIndex, this.items.length - 1);
                }
            } catch (err) {
                console.error(err);
                alert("Delete failed.");
            } finally {
                this.deleting = false;
            }
        },
    };
};
