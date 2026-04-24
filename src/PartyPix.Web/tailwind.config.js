/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Pages/**/*.{cshtml,razor,html}",
    "./Areas/**/*.{cshtml,razor,html}",
    "./Views/**/*.{cshtml,razor,html}",
    "./wwwroot/js/**/*.js",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
};
