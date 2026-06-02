export type PracticeItem = {
  type: 'Проверка' | 'Свободное';
  text: string;
};

export type LessonQuizOption = {
  id: string;
  text: string;
  isCorrect: boolean;
};

export type LessonQuizQuestion = {
  id: string;
  question: string;
  options: LessonQuizOption[];
};

export type LessonQuiz = {
  question?: string;
  options?: LessonQuizOption[];
  questions?: LessonQuizQuestion[];
  correctMessage: string;
  incorrectMessage: string;
};

export type LessonItem = {
  id: number;
  title: string;
  summary: string;
  duration: string;
  format: 'theory' | 'practice';
  theory: string[];
  cheatSheet?: string[];
  cheatSheetTitle?: string;
  videoUrl?: string;
  quiz?: LessonQuiz;
  task?: string;
  successHint?: string;
};

export type ModuleItem = {
  id: string;
  order: number;
  title: string;
  goal: string;
  lessons: LessonItem[];
  practice: PracticeItem[];
};

export const courseModules: ModuleItem[] = [
  {
    id: '1',
    order: 1,
    title: 'Первые шаги в Unity и настройка инструментов',
    goal: 'Снять страх перед интерфейсом движка и подготовить рабочую среду.',
    lessons: [
      {
        id: 1,
        title: 'Урок 1.1: Установка Unity',
        summary: 'Загрузка Unity Hub, выбор версии редактора и создание первого 2D/3D проекта.',
        duration: '3 мин',
        format: 'theory',
        videoUrl: 'https://rutube.ru/play/embed/3b978fd326d9b0434ffc07509de92ea6',
        theory: [
          'Unity Hub нужен для установки редактора, управления версиями Unity и быстрого запуска проектов.',
          'Для обучения лучше выбирать стабильную версию: LTS или Unity 6, чтобы избежать лишних проблем совместимости.',
          'При создании первого проекта важно сразу определить, будет он 2D или 3D, и проверить базовую структуру проекта.',
        ],
        cheatSheet: [
          'Установить Unity Hub с сайта unity.com.',
          'Установить Unity 6 через Hub: Installs -> Install Editor -> выбрать Unity 6 -> добавить модуль сборки для вашей ОС.',
          'Активировать лицензию Unity Personal.',
          'Создать проект: Projects -> New Project -> выбрать шаблон 2D URP -> указать имя и место -> создать.',
          'Убедиться, что редактор открылся и нет ошибок.',
        ],
        quiz: {
          questions: [
            {
              id: 'hub',
              question: 'Какой инструмент используется для управления версиями Unity и проектами?',
              options: [
                { id: 'hub_a', text: 'Unity Editor', isCorrect: false },
                { id: 'hub_b', text: 'Unity Hub', isCorrect: true },
                { id: 'hub_c', text: 'Package Manager', isCorrect: false },
                { id: 'hub_d', text: 'Visual Studio', isCorrect: false },
              ],
            },
            {
              id: 'template',
              question: 'Какой шаблон проекта мы выбрали для создания первого проекта?',
              options: [
                { id: 'template_a', text: '3D Core', isCorrect: false },
                { id: 'template_b', text: '2D Core', isCorrect: false },
                { id: 'template_c', text: '2D URP', isCorrect: true },
                { id: 'template_d', text: 'Universal 3D', isCorrect: false },
              ],
            },
            {
              id: 'check',
              question: 'Что нужно сделать после установки редактора, чтобы убедиться в его работоспособности?',
              options: [
                { id: 'check_a', text: 'Создать новый C# скрипт', isCorrect: false },
                { id: 'check_b', text: 'Импортировать плагин', isCorrect: false },
                { id: 'check_c', text: 'Открыть окно Console и проверить отсутствие красных ошибок', isCorrect: true },
                { id: 'check_d', text: 'Нажать кнопку Play', isCorrect: false },
              ],
            },
          ],
          correctMessage: 'Верно.',
          incorrectMessage: 'Неверно.',
        },
      },
      {
        id: 2,
        title: 'Урок 1.2: Обзор интерфейса Unity',
        summary: 'Назначение окон Scene, Hierarchy, Inspector и Project и их роль в ежедневной работе.',
        duration: '2 мин',
        format: 'theory',
        videoUrl: 'https://rutube.ru/play/embed/a5d73134a5e089301a5756816c750af9',
        theory: [
          'Scene - окно редактирования сцены: размещение, перемещение и изменение объектов в мире.',
          'Game - предварительный просмотр игры с точки зрения камеры, то есть то, что увидит игрок.',
          'Hierarchy показывает список всех объектов текущей сцены и их вложенность.',
          'Inspector показывает параметры выбранного объекта: компоненты, свойства и настройки.',
          'Project отображает файловую систему проекта (Assets): скрипты, сцены, текстуры и префабы.',
          'Console выводит сообщения, предупреждения и ошибки, которые помогают при отладке.',
          'Окна можно перетаскивать и закреплять (dock), а открывать через меню Window.',
          'На верхней панели инструментов используются Hand, Move, Rotate, Scale, Rect, а также Play и Pause.',
          'В режиме Play изменения в редакторе после остановки не сохраняются.',
        ],
        cheatSheet: [
          'Scene - редактирование мира.',
          'Game - вид от камеры.',
          'Hierarchy - список всех объектов на сцене.',
          'Inspector - настройки выделенного объекта (Transform и др.).',
          'Project - файлы проекта (Assets).',
          'Console - сообщения и ошибки.',
          'Move / Rotate / Scale - инструменты трансформации.',
          'Play (>) - запуск игры.',
        ],
        quiz: {
          questions: [
            {
              id: 'hierarchy_window',
              question: 'В каком окне Unity отображается список всех объектов на текущей сцене?',
              options: [
                { id: 'hierarchy_window_a', text: 'Scene', isCorrect: false },
                { id: 'hierarchy_window_b', text: 'Game', isCorrect: false },
                { id: 'hierarchy_window_c', text: 'Hierarchy', isCorrect: true },
                { id: 'hierarchy_window_d', text: 'Project', isCorrect: false },
              ],
            },
            {
              id: 'play_button',
              question: 'Какая кнопка на панели инструментов отвечает за запуск игры в редакторе?',
              options: [
                { id: 'play_button_a', text: 'Pause', isCorrect: false },
                { id: 'play_button_b', text: 'Play', isCorrect: true },
                { id: 'play_button_c', text: 'Step', isCorrect: false },
                { id: 'play_button_d', text: 'Hand Tool', isCorrect: false },
              ],
            },
            {
              id: 'play_mode_changes',
              question: 'Что происходит с изменениями, сделанными в режиме игры (Play mode), после его остановки?',
              options: [
                { id: 'play_mode_changes_a', text: 'Изменения сохраняются автоматически', isCorrect: false },
                { id: 'play_mode_changes_b', text: 'Изменения сохраняются только если нажать Save', isCorrect: false },
                { id: 'play_mode_changes_c', text: 'Изменения не сохраняются', isCorrect: true },
                { id: 'play_mode_changes_d', text: 'Изменения сохраняются в отдельный файл', isCorrect: false },
              ],
            },
          ],
          correctMessage: 'Верно.',
          incorrectMessage: 'Неверно.',
        },
      },
    ],
    practice: [
      {
        type: 'Проверка',
        text: 'Тест: «В каком окне Unity изменяются параметры выделенного объекта?»',
      },
      {
        type: 'Свободное',
        text: 'Создать на сцене куб, сферу и источник направленного света. Назначить каждому объекту материал с уникальным цветом.',
      },
    ],
  },
];

export const getModuleById = (id: string) => courseModules.find((module) => module.id === id);

export const getLessonById = (lessonId: number) =>
  courseModules
    .flatMap((module) => module.lessons.map((lesson) => ({ module, lesson })))
    .find((item) => item.lesson.id === lessonId);

export const allLessonIds = courseModules.flatMap((module) => module.lessons.map((lesson) => lesson.id));
