import { readFile, mkdir, writeFile, copyFile } from 'node:fs/promises';
const root = new URL('../../', import.meta.url);
const books = JSON.parse(await readFile(new URL('addin/portable/Calculations/Workbooks.json', root), 'utf8'));
const labels = ['Стенка трубы', 'Длина болта', 'Балка', 'Болты и швы', 'Высота узла'];
const target = new URL('../public/data/', import.meta.url);
await mkdir(target, { recursive: true });
for (let i = 0; i < books.length; i++) await writeFile(new URL(`${i}.json`, target), JSON.stringify(books[i]));
await writeFile(new URL('catalog.json', target), JSON.stringify(books.map((b, id) => ({ id, title: labels[id], source: b.Source, sha256: b.Sha256 }))));
const fonts = new URL('../public/fonts/', import.meta.url);
await mkdir(fonts, { recursive: true });
for (const file of ['SourceSans3-Regular.ttf', 'SourceSans3-Semibold.ttf', 'LICENSE.md']) await copyFile(new URL(`addin/portable/Fonts/${file}`, root), new URL(file, fonts));
console.log('Prepared five workbooks and bundled fonts');

await copyFile(new URL('addin/portable/Assets/AppIcon.ico', root), new URL('../public/favicon.ico', import.meta.url));
