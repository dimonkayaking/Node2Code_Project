import React from 'react';
import { Link } from 'react-router-dom';
import { useAppContext } from '../context/AppContext';
import './Landing.css';

const Landing: React.FC = () => {
  const { user } = useAppContext();
  const learningCtaLink = user ? '/instructions' : '/register';

  return (
    <div className="landing-page">
      <section className="landing-hero">
        <div className="landing-hero__content">
          <span className="landing-badge">Плагин + встроенная инструкция для Unity</span>
          <h1>
            Мост между визуальным <span className="no-wrap">программированием</span> и кодом в Unity
          </h1>
          <p className="landing-hero__lead">
            Платформа помогает работать с нодами и C# в обе стороны, а встроенная инструкция проводит
            пользователя от установки Unity до подключения плагина и первого знакомства с редактором.
          </p>

          <div className="landing-hero__actions">
            <Link to={learningCtaLink} className="landing-button landing-button--primary">
              Начать работу
            </Link>
          </div>

          <ul className="landing-hero__stats">
            <li>
              <strong>Установщик и документация</strong>
            </li>
            <li>
              <strong>Инструкция</strong>
            </li>
            <li>
              <strong>Ноды &lt;--&gt; C#</strong>
            </li>
          </ul>
        </div>

        <div className="landing-hero__visual">
          <div className="hero-panel hero-panel--graph">
            <div className="hero-panel__label">Визуальная логика</div>
            <div className="hero-node hero-node--accent">On Start</div>
            <div className="hero-link" />
            <div className="hero-node">Speed: 5</div>
            <div className="hero-link" />
            <div className="hero-node hero-node--success">Move Forward</div>
          </div>

          <div className="hero-panel hero-panel--code">
            <div className="hero-panel__label">C# представление</div>
            <pre>{`void Update()
{
    transform.Translate(
        Vector3.forward * speed * Time.deltaTime
    );
}`}</pre>
          </div>
        </div>
      </section>

      <section className="landing-section landing-section--final">
        <div className="final-cta">
          <div>
            <span className="landing-eyebrow">Для кого это</span>
            <h2>Для тех, кто хочет понять Unity глубже, не теряя наглядность</h2>
            <p>
              Сайт одновременно объясняет продукт и даёт короткую инструкцию: здесь удобно скачать
              плагин, открыть нужный раздел и сразу перейти к практическому старту.
            </p>
          </div>

        </div>
      </section>
    </div>
  );
};

export default Landing;
