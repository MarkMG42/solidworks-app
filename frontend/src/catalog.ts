import demo from "../../config/macros.demo.json";

export interface Macro {
  id: string;
  name: string;
  category: string;
  description: string;
  details: string;
  file: string;
  module: string;
  procedure: string;
  requiredDocument: string;
  icon: string;
}

export const macros: Macro[] = demo.macros;
export const categories = [
  "Чертежи",
  "Детали",
  "Сборки",
  "Сварные конструкции",
  "Экспорт",
  "Другое",
];
