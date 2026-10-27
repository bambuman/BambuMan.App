window.bambuman = {
    // Where the page was before a read brought the edit card up, to go back there once it closes.
    scrollBeforeEdit: null,

    // Focus a freshly read spool's weight field with its value selected, then bring its card — and the message about
    // the read above it — to the top of the screen: the keyboard that opens next shrinks the web view and would
    // otherwise cover most of the card.
    focusSpoolEdit: function (input) {
        if (!input) return;

        // Only the first read of a run of reads remembers the position; the card is already up for the next ones.
        if (!document.body.classList.contains("editing")) window.bambuman.scrollBeforeEdit = window.scrollY;

        // Leave room below the card so it can scroll all the way up, whatever follows it on the page.
        document.body.classList.add("editing");

        input.focus({ preventScroll: true });
        if (typeof input.select === "function") input.select();

        const card = input.closest(".card") || input;

        // The success / error / info banners sit right above the card; include them so the outcome stays in view.
        const target = () => {
            let element = card;

            while (element.previousElementSibling && element.previousElementSibling.classList.contains("banner")) element = element.previousElementSibling;

            return element;
        };

        const scroll = () => target().scrollIntoView({ block: "start", behavior: "smooth" });

        scroll();

        // Again once the keyboard has opened and the view has been resized, and once more for a message that arrives
        // after the card.
        setTimeout(scroll, 400);
        setTimeout(scroll, 900);
    },

    // The edit card closed (save or cancel): drop the keyboard and go back to where the page was before the read.
    blur: function () {
        document.body.classList.remove("editing");

        if (document.activeElement && typeof document.activeElement.blur === "function") document.activeElement.blur();

        const top = window.bambuman.scrollBeforeEdit;
        window.bambuman.scrollBeforeEdit = null;

        if (top === null) return;

        window.scrollTo({ top: top });

        // Again once the keyboard has closed and the view has its full height back.
        setTimeout(() => window.scrollTo({ top: top }), 350);
    }
};

// Tapping into a form field selects its value, so typing replaces it instead of having to clear it first. Deferred,
// because the tap itself places the caret after focus and would undo an immediate select. Date fields have no text
// selection.
document.addEventListener("focusin", function (event) {
    const input = event.target;

    if (!(input instanceof HTMLInputElement) || !input.closest(".input-wrap")) return;
    if (input.type === "date" || input.type === "checkbox") return;

    setTimeout(function () {
        if (document.activeElement === input) input.select();
    }, 50);
});
