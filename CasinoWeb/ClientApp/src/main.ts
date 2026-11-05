import { startApp } from '@/app';

import './style.css';

const root = document.querySelector<HTMLElement>('#app');

if (root)
  void startApp(root);
