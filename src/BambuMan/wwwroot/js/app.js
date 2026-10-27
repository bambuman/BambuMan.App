window.bambuman = {
    // Focus a freshly read spool's weight field with its value selected, then bring its card to the top of the
    // screen: the keyboard that opens next shrinks the web view and would otherwise cover most of the card.
    focusSpoolEdit: function (input) {
        if (!input) return;

        // Leave room below the card so it can scroll all the way up, whatever follows it on the page.
        document.body.classList.add("editing");

        input.focus({ preventScroll: true });
        if (typeof input.select === "function") input.select();

        const card = input.closest(".card") || input;
        const scrollToCard = () => card.scrollIntoView({ block: "start", behavior: "smooth" });

        scrollToCard();

        // Again once the keyboard has finished opening and the view has been resized.
        setTimeout(scrollToCard, 400);
    },

    blur: function () {
        document.body.classList.remove("editing");

        if (document.activeElement && typeof document.activeElement.blur === "function") document.activeElement.blur();
    }
};
