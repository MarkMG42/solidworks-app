// Original XLSX bytes and edit overlays stay on this browser/device.
let database;const queues=new Map(),deleted=new Set();
async function db(){
 if(!database)database=new Promise((resolve,reject)=>{const r=indexedDB.open('sw-assistant-xlsx-library',1);r.onupgradeneeded=()=>{r.result.createObjectStore('books',{keyPath:'id'});r.result.createObjectStore('metadata',{keyPath:'id'});};r.onsuccess=()=>{r.result.onversionchange=()=>{r.result.close();database=null;};resolve(r.result);};r.onerror=()=>{database=null;reject(r.error);};});
 return database;
}
async function read(store,key){const d=await db();return new Promise((resolve,reject)=>{const tx=d.transaction(store),r=key==null?tx.objectStore(store).getAll():tx.objectStore(store).get(key);r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});}
export const listBooks=()=>read('metadata');
export const getBook=id=>read('books',id);
function enqueue(id,action){const next=(queues.get(id)||Promise.resolve()).catch(()=>{}).then(action);queues.set(id,next);return next;}
export function saveBook(record){return enqueue(record.id,async()=>{
 if(deleted.has(record.id))throw new Error('Книга удалена');
 const d=await db();return new Promise((resolve,reject)=>{const tx=d.transaction(['books','metadata'],'readwrite');tx.objectStore('books').put(record);tx.objectStore('metadata').put({id:record.id,title:record.title,fileName:record.fileName,created:record.created});tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||new Error('Сохранение прервано'));});
});}
export function deleteBook(id){deleted.add(id);return enqueue(id,async()=>{const d=await db();return new Promise((resolve,reject)=>{const tx=d.transaction(['books','metadata'],'readwrite');tx.objectStore('books').delete(id);tx.objectStore('metadata').delete(id);tx.oncomplete=resolve;tx.onerror=()=>{deleted.delete(id);reject(tx.error);};tx.onabort=()=>{deleted.delete(id);reject(tx.error||new Error('Удаление прервано'));};});});}
