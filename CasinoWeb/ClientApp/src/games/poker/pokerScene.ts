import Phaser from 'phaser';

import type { PokerStage } from '@/games/poker/pokerGame';
import type { Card, PokerHand, PokerSeat, TableSeat, TableView } from '@/protocol';

import { colorNumber, drawCard, themeToken } from '@/games/cards/cardDrawing';
import { CENTER, seatPosition } from '@/games/poker/layout';
import { formatTokens } from '@/games/slots/slotsGame';

const BOARD_CARD = { height: 62, width: 44 };
const HOLE_CARD = { height: 56, width: 40 };
const SEAT_BOX = { height: 118, width: 140 };
const BOARD_STEP = 50;
const FONT = 'system-ui, sans-serif';

const STREET_NAMES: Record<PokerHand['street'], string> = { flop: 'FLOP', preflop: 'PRE-FLOP', river: 'RIVER', showdown: 'SHOWDOWN', turn: 'TURN' };

/**
 * The poker table: the seats around an oval, the board and the pot in the middle. It draws the view it is given and
 * decides nothing; every change of the view redraws the whole table. Whose turn it is shows as a thicker seat outline
 * and the words TO ACT, never by color alone.
 */
export class PokerScene extends Phaser.Scene implements PokerStage {
  private layer!: Phaser.GameObjects.Container;
  private readonly onReady: (stage: PokerStage) => void;

  constructor(onReady: (stage: PokerStage) => void) {
    super('poker');
    this.onReady = onReady;
  }

  create(): void {
    const table = this.add.ellipse(CENTER.x, CENTER.y, 470, 270, colorNumber(themeToken('--color-bg')));
    table.setStrokeStyle(3, colorNumber(themeToken('--color-border')));
    this.layer = this.add.container(0, 0);
    this.onReady(this);
  }

  show(view: TableView | null): void {
    this.layer.removeAll(true);
    if (!view)
      return;
    for (let seat = 0; seat < view.maxSeats; seat++)
      this.drawSeat(view, seat);
    this.drawMiddle(view);
  }

  private text(x: number, y: number, content: string, size: number, bold = false, color = themeToken('--color-text')): Phaser.GameObjects.Text {
    const text = this.add.text(x, y, content, { color, fontFamily: FONT, fontSize: `${size}px`, fontStyle: bold ? 'bold' : 'normal' }).setOrigin(0.5);
    this.layer.add(text);
    return text;
  }

  private card(x: number, y: number, card: Card | null, size: { height: number; width: number }): void {
    const container = this.add.container(x, y);
    container.add(drawCard(this, card, size));
    this.layer.add(container);
  }

  private drawMiddle(view: TableView): void {
    const { hand } = view;
    if (!hand) {
      this.text(CENTER.x, CENTER.y, view.nextHandSecondsLeft === null ? 'Waiting for players' : 'Next hand soon', 18, true);
      return;
    }
    this.text(CENTER.x, CENTER.y + 56, `${STREET_NAMES[hand.street]}   Pot ${formatTokens(hand.pot)}`, 15, true);
    const start = CENTER.x - (4 * BOARD_STEP) / 2;
    for (let index = 0; index < 5; index++) {
      const dealt = hand.board[index];
      if (dealt)
        this.card(start + index * BOARD_STEP, CENTER.y, dealt, BOARD_CARD);
      else
        this.empty(start + index * BOARD_STEP, CENTER.y, BOARD_CARD);
    }
  }

  private empty(x: number, y: number, size: { height: number; width: number }): void {
    const slot = this.add.rectangle(x, y, size.width, size.height).setStrokeStyle(1, colorNumber(themeToken('--color-border')));
    this.layer.add(slot);
  }

  private drawSeat(view: TableView, seat: number): void {
    const at = seatPosition(seat, view.mySeat, view.maxSeats);
    const taken = view.seats.find(s => s.seat === seat);
    const border = colorNumber(themeToken('--color-border'));
    if (!taken) {
      const box = this.add.rectangle(at.x, at.y, SEAT_BOX.width, 44).setStrokeStyle(1, border);
      this.layer.add(box);
      this.text(at.x, at.y, `Seat ${seat + 1}: empty`, 13, false, themeToken('--color-muted'));
      return;
    }

    const inHand = view.hand?.seats.find(s => s.seat === seat);
    const toAct = view.turnSeat === seat;
    const dim = inHand?.folded ? 0.5 : 1;
    const box = this.add.rectangle(at.x, at.y, SEAT_BOX.width, SEAT_BOX.height, colorNumber(themeToken('--color-surface')));
    box.setStrokeStyle(toAct ? 5 : 1, toAct ? colorNumber(themeToken('--color-accent')) : border);
    this.layer.add(box);
    const name = `${taken.name.length > 14 ? `${taken.name.slice(0, 13)}…` : taken.name}${seat === view.mySeat ? ' (you)' : ''}`;
    this.text(at.x, at.y - 48, name, 13, true).setAlpha(dim);
    this.drawHole(inHand, at.x, at.y - 8, dim);
    this.text(at.x, at.y + 30, this.statusLine(view, taken, inHand), 12, toAct, themeToken('--color-muted'));
    if (inHand && inHand.roundBet > 0)
      this.text(at.x, at.y + 47, `Bet ${formatTokens(inHand.roundBet)}`, 13, true);
    if (view.hand?.buttonSeat === seat) {
      // On the corner of the seat, so it never covers its name or cards.
      const disc = this.add.circle(at.x + SEAT_BOX.width / 2, at.y - SEAT_BOX.height / 2, 11, 0xFFFFFF).setStrokeStyle(2, border);
      this.layer.add(disc);
      this.text(at.x + SEAT_BOX.width / 2, at.y - SEAT_BOX.height / 2, 'D', 13, true, '#1f2328');
    }
  }

  private drawHole(inHand: PokerSeat | undefined, x: number, y: number, alpha: number): void {
    if (!inHand || inHand.folded)
      return;
    const cards: (Card | null)[] = inHand.holeCards ?? [null, null];
    cards.forEach((hole, index) => {
      const container = this.add.container(x + (index === 0 ? -23 : 23), y);
      container.add(drawCard(this, hole, HOLE_CARD));
      container.setAlpha(alpha);
      this.layer.add(container);
    });
  }

  /** The seat's state in words: who acts, the blinds and the button, folded, all in. */
  private statusLine(view: TableView, taken: TableSeat, inHand: PokerSeat | undefined): string {
    const parts: string[] = [];
    if (view.turnSeat === taken.seat)
      parts.push('TO ACT');
    const won = view.hand?.result?.winnings.find(w => w.seat === taken.seat)?.amount ?? 0;
    if (won > 0)
      parts.push(`won ${formatTokens(won)}`);
    if (inHand?.folded)
      parts.push('folded');
    else if (inHand?.allIn)
      parts.push('all in');
    if (view.hand?.buttonSeat === taken.seat)
      parts.push('dealer');
    if (view.hand?.smallBlindSeat === taken.seat)
      parts.push('SB');
    if (view.hand?.bigBlindSeat === taken.seat)
      parts.push('BB');
    if (taken.isAi)
      parts.push('AI');
    if (taken.away)
      parts.push('away');
    return parts.join(' · ');
  }
}
